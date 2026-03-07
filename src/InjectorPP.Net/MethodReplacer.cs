using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace InjectorPP.Net;

/// <summary>
/// Core engine that handles method redirection.
///
/// On .NET Core, GetFunctionPointer() returns a precode address containing JMP [fixup_cell].
/// Both the precode and caller call sites share the same fixup cell. By modifying the fixup cell
/// value (a pointer swap), we redirect all calls to the replacement method.
///
/// If the precode format can't be parsed (or for platforms where it differs), we fall back to
/// patching the resolved JIT-compiled code directly.
/// </summary>
internal static class MethodReplacer
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FlushInstructionCache(IntPtr hProcess, IntPtr lpBaseAddress, UIntPtr dwSize);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    private const uint PAGE_EXECUTE_READWRITE = 0x40;

    [DllImport("libc", EntryPoint = "mprotect", SetLastError = true)]
    private static extern int LinuxMProtect(IntPtr addr, UIntPtr len, int prot);

    private const int PROT_READ = 1;
    private const int PROT_WRITE = 2;
    private const int PROT_EXEC = 4;

    /// <summary>
    /// Replaces the original method by redirecting calls to the replacement method.
    /// Uses fixup cell modification (pointer swap) when possible, falling back to
    /// JIT code patching when the precode format is not recognized.
    /// </summary>
    public static MethodReplacement Replace(MethodBase original, MethodInfo replacement)
    {
        RuntimeHelpers.PrepareMethod(original.MethodHandle);
        RuntimeHelpers.PrepareMethod(replacement.MethodHandle);

        IntPtr originalFuncPtr = original.MethodHandle.GetFunctionPointer();
        IntPtr replacementFuncPtr = replacement.MethodHandle.GetFunctionPointer();

        // Try fixup cell approach first (cleanest - just a pointer swap)
        IntPtr fixupCell = FindFixupCell(originalFuncPtr);
        if (fixupCell != IntPtr.Zero)
        {
            return PatchFixupCell(original, fixupCell, replacementFuncPtr);
        }

        // Fallback: patch the JIT code directly
        IntPtr codeAddr = ResolveJitCodeAddress(originalFuncPtr);
        return PatchJitCode(original, codeAddr, replacementFuncPtr);
    }

    /// <summary>
    /// Restores the original method.
    /// </summary>
    public static void Restore(MethodReplacement replacement)
    {
        if (replacement.IsFixupCellPatch)
        {
            // Restore the original fixup cell value
            MakeMemoryWritable(replacement.OriginalAddress);
            Marshal.Copy(replacement.OriginalBytes, 0, replacement.OriginalAddress, 8);
        }
        else
        {
            // Restore the original JIT code bytes
            MakeMemoryWritable(replacement.OriginalAddress);
            Marshal.Copy(replacement.OriginalBytes, 0, replacement.OriginalAddress, replacement.PatchSize);
            FlushCache(replacement.OriginalAddress, replacement.PatchSize);
        }
    }

    /// <summary>
    /// Finds the fixup cell address from an x64 precode (JMP [RIP+disp32]).
    /// Returns IntPtr.Zero if the precode format is not recognized.
    /// </summary>
    private static IntPtr FindFixupCell(IntPtr funcPtr)
    {
        var arch = RuntimeInformation.ProcessArchitecture;

        if (arch == Architecture.X64)
        {
            byte[] header = new byte[6];
            Marshal.Copy(funcPtr, header, 0, 6);

            if (header[0] == 0xFF && header[1] == 0x25)
            {
                // JMP [RIP+disp32] - the fixup cell is at RIP + 6 + disp32
                int disp = BitConverter.ToInt32(header, 2);
                return new IntPtr(funcPtr.ToInt64() + 6 + disp);
            }
        }
        else if (arch == Architecture.Arm64)
        {
            // ARM64 precodes use ADRP+LDR+BR pattern
            byte[] precodeBytes = new byte[12];
            Marshal.Copy(funcPtr, precodeBytes, 0, 12);

            uint instr1 = BitConverter.ToUInt32(precodeBytes, 0);
            uint instr2 = BitConverter.ToUInt32(precodeBytes, 4);
            uint instr3 = BitConverter.ToUInt32(precodeBytes, 8);

            // Check for ADRP + LDR + BR Xn pattern
            if ((instr1 & 0x9F000000) == 0x90000000 &&  // ADRP
                (instr2 & 0xFFC00000) == 0xF9400000 &&  // LDR (unsigned offset)
                (instr3 & 0xFFFFFC00) == 0xD61F0000)    // BR
            {
                uint immlo = (instr1 >> 29) & 0x3;
                uint immhi = (instr1 >> 5) & 0x7FFFF;
                long pageOffset = (long)((immhi << 2) | immlo) << 12;
                if ((pageOffset & 0x100000000L) != 0)
                    pageOffset |= unchecked((long)0xFFFFFFFF00000000L);

                long basePage = funcPtr.ToInt64() & ~0xFFFL;
                long targetPage = basePage + pageOffset;

                uint imm12 = (instr2 >> 10) & 0xFFF;
                long ldrOffset = (long)imm12 * 8;

                return new IntPtr(targetPage + ldrOffset);
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Patches the fixup cell to redirect the method to the replacement's entry point.
    /// This is a simple pointer swap in a data section - no code modification needed.
    /// </summary>
    private static MethodReplacement PatchFixupCell(MethodBase original, IntPtr fixupCell, IntPtr replacementFuncPtr)
    {
        MakeMemoryWritable(fixupCell);

        // Save the original cell value (JIT code address)
        byte[] originalCellValue = new byte[8];
        Marshal.Copy(fixupCell, originalCellValue, 0, 8);

        // Write the replacement's function pointer to the fixup cell
        byte[] newCellValue = BitConverter.GetBytes(replacementFuncPtr.ToInt64());
        Marshal.Copy(newCellValue, 0, fixupCell, 8);

        return new MethodReplacement
        {
            OriginalMethod = original,
            OriginalBytes = originalCellValue,
            OriginalAddress = fixupCell,
            PatchSize = 8,
            IsFixupCellPatch = true
        };
    }

    /// <summary>
    /// Patches the JIT-compiled code with a JMP trampoline. Used as fallback when
    /// fixup cell approach is not available.
    /// </summary>
    private static MethodReplacement PatchJitCode(MethodBase original, IntPtr codeAddr, IntPtr replacementFuncPtr)
    {
        // Resolve the replacement's actual code address too
        IntPtr replacementCodeAddr = ResolveJitCodeAddress(replacementFuncPtr);

        MakeMemoryWritable(codeAddr);

        byte[] jmpBytes = GenerateJmpTrampoline(replacementCodeAddr);

        byte[] originalBytes = new byte[jmpBytes.Length];
        Marshal.Copy(codeAddr, originalBytes, 0, jmpBytes.Length);

        Marshal.Copy(jmpBytes, 0, codeAddr, jmpBytes.Length);
        FlushCache(codeAddr, jmpBytes.Length);

        return new MethodReplacement
        {
            OriginalMethod = original,
            OriginalBytes = originalBytes,
            OriginalAddress = codeAddr,
            PatchSize = jmpBytes.Length,
            IsFixupCellPatch = false
        };
    }

    /// <summary>
    /// Resolves the actual JIT-compiled code address by following precode indirections.
    /// </summary>
    private static IntPtr ResolveJitCodeAddress(IntPtr funcPtr)
    {
        var arch = RuntimeInformation.ProcessArchitecture;

        if (arch == Architecture.X64)
        {
            byte[] header = new byte[6];
            Marshal.Copy(funcPtr, header, 0, 6);

            if (header[0] == 0xFF && header[1] == 0x25)
            {
                int disp = BitConverter.ToInt32(header, 2);
                IntPtr fixupCell = new IntPtr(funcPtr.ToInt64() + 6 + disp);

                byte[] targetBytes = new byte[8];
                Marshal.Copy(fixupCell, targetBytes, 0, 8);
                return new IntPtr(BitConverter.ToInt64(targetBytes, 0));
            }
            else if (header[0] == 0xE9)
            {
                int disp = BitConverter.ToInt32(header, 1);
                return new IntPtr(funcPtr.ToInt64() + 5 + disp);
            }
        }

        return funcPtr;
    }

    private static void MakeMemoryWritable(IntPtr address)
    {
        long pageSize = Environment.SystemPageSize;
        IntPtr pageAligned = new IntPtr(address.ToInt64() & ~(pageSize - 1));
        UIntPtr size = new UIntPtr((uint)pageSize);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (!VirtualProtect(pageAligned, size, PAGE_EXECUTE_READWRITE, out _))
            {
                int error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException(
                    $"VirtualProtect failed at 0x{pageAligned.ToInt64():X}. Error code: {error}");
            }
        }
        else
        {
            if (LinuxMProtect(pageAligned, size, PROT_READ | PROT_WRITE | PROT_EXEC) != 0)
            {
                throw new InvalidOperationException(
                    $"mprotect failed. Error code: {Marshal.GetLastWin32Error()}");
            }
        }
    }

    private static void FlushCache(IntPtr address, int size)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            FlushInstructionCache(GetCurrentProcess(), address, new UIntPtr((uint)size));
        }
    }

    private static byte[] GenerateJmpTrampoline(IntPtr target)
    {
        var arch = RuntimeInformation.ProcessArchitecture;

        if (arch == Architecture.X64)
        {
            byte[] jmp = new byte[12];
            jmp[0] = 0x48; jmp[1] = 0xB8;
            BitConverter.GetBytes(target.ToInt64()).CopyTo(jmp, 2);
            jmp[10] = 0xFF; jmp[11] = 0xE0;
            return jmp;
        }
        else if (arch == Architecture.Arm64)
        {
            byte[] jmp = new byte[20];
            long addr = target.ToInt64();

            uint movz = 0xD2800009u | ((uint)(addr & 0xFFFF) << 5);
            BitConverter.GetBytes(movz).CopyTo(jmp, 0);

            uint movk1 = 0xF2A00009u | ((uint)((addr >> 16) & 0xFFFF) << 5);
            BitConverter.GetBytes(movk1).CopyTo(jmp, 4);

            uint movk2 = 0xF2C00009u | ((uint)((addr >> 32) & 0xFFFF) << 5);
            BitConverter.GetBytes(movk2).CopyTo(jmp, 8);

            uint movk3 = 0xF2E00009u | ((uint)((addr >> 48) & 0xFFFF) << 5);
            BitConverter.GetBytes(movk3).CopyTo(jmp, 12);

            uint br = 0xD61F0120u;
            BitConverter.GetBytes(br).CopyTo(jmp, 16);

            return jmp;
        }
        else if (arch == Architecture.X86)
        {
            byte[] jmp = new byte[6];
            jmp[0] = 0x68;
            BitConverter.GetBytes(target.ToInt32()).CopyTo(jmp, 1);
            jmp[5] = 0xC3;
            return jmp;
        }
        else
        {
            throw new PlatformNotSupportedException(
                $"Architecture {arch} is not supported by InjectorPP.Net.");
        }
    }
}

/// <summary>
/// Stores information needed to restore an original method after injection.
/// </summary>
internal sealed class MethodReplacement
{
    public MethodBase OriginalMethod { get; init; } = null!;
    public byte[] OriginalBytes { get; init; } = null!;
    public IntPtr OriginalAddress { get; init; }
    public int PatchSize { get; init; }
    public bool IsFixupCellPatch { get; init; }
}
