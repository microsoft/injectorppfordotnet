using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace InjectorPP.Net;

/// <summary>
/// Core engine that handles method redirection with thread-local dispatch.
///
/// When a method is first patched on any thread, it is redirected to a dispatcher.
/// The dispatcher looks up the current thread's replacement in ThreadLocalRegistry.
/// If no replacement is registered for the calling thread, the original method is called.
///
/// This enables parallel test execution — each thread can independently
/// fake the same method to different values without interference.
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
    /// Tracks globally patched methods. Key is the original method handle.
    /// This ensures each method is only patched once (to the dispatcher),
    /// regardless of how many threads register replacements.
    /// </summary>
    private static readonly ConcurrentDictionary<RuntimeMethodHandle, PatchedMethodInfo> s_patchedMethods = new();
    private static readonly object s_patchLock = new();

    /// <summary>
    /// Registers a thread-local replacement for the given method.
    /// If this is the first replacement for this method, patches it to a dispatcher.
    /// Returns a MethodReplacement that can be used to undo this thread's registration.
    /// </summary>
    public static MethodReplacement Replace(MethodBase original, MethodInfo replacement)
    {
        RuntimeHelpers.PrepareMethod(original.MethodHandle);
        RuntimeHelpers.PrepareMethod(replacement.MethodHandle);

        IntPtr replacementFuncPtr = replacement.MethodHandle.GetFunctionPointer();
        IntPtr replacementCodeAddr = ResolveJitCodeAddress(replacementFuncPtr);

        // Lock ensures atomicity of GetOrCreatePatch + AddReplacement,
        // preventing a race where Restore removes the patch between these two steps.
        lock (s_patchLock)
        {
            var patchedInfo = GetOrCreatePatchLocked(original);
            ThreadLocalRegistry.AddReplacement(patchedInfo.MethodKey, replacementCodeAddr);

            return new MethodReplacement
            {
                OriginalMethod = original,
                MethodKey = patchedInfo.MethodKey,
            };
        }
    }

    /// <summary>
    /// Removes the current thread's replacement for the given method.
    /// If this was the last thread using this method's dispatch, restores the original.
    /// </summary>
    public static void Restore(MethodReplacement replacement)
    {
        lock (s_patchLock)
        {
            bool isLast = ThreadLocalRegistry.RemoveReplacement(replacement.MethodKey);

            if (isLast)
            {
                if (s_patchedMethods.TryRemove(replacement.OriginalMethod.MethodHandle, out var patchedInfo))
                {
                    RestoreOriginalBytes(patchedInfo);
                    ThreadLocalRegistry.RemoveMethod(replacement.MethodKey);
                }
            }
        }
    }

    /// <summary>
    /// Gets or creates a patch entry for the given method.
    /// Must be called while holding s_patchLock.
    /// </summary>
    private static PatchedMethodInfo GetOrCreatePatchLocked(MethodBase original)
    {
        if (s_patchedMethods.TryGetValue(original.MethodHandle, out var existing))
            return existing;

        IntPtr originalFuncPtr = original.MethodHandle.GetFunctionPointer();

        // Determine original function pointer for "call original" path
        IntPtr originalCodeAddr;
        IntPtr trampolineAddr = IntPtr.Zero;
        bool isFixupCell;

        IntPtr fixupCell = FindFixupCell(originalFuncPtr);
        if (fixupCell != IntPtr.Zero)
        {
            // Fixup cell path: original code is untouched, read its address from the cell
            byte[] cellBytes = new byte[8];
            Marshal.Copy(fixupCell, cellBytes, 0, 8);
            originalCodeAddr = new IntPtr(BitConverter.ToInt64(cellBytes, 0));
            isFixupCell = true;
        }
        else
        {
            // JIT code patching path: need a trampoline since we'll overwrite the code
            IntPtr codeAddr = ResolveJitCodeAddress(originalFuncPtr);
            int patchSize = GetPatchSize();
            trampolineAddr = TrampolineAllocator.CreateTrampoline(codeAddr, patchSize);
            originalCodeAddr = trampolineAddr;
            isFixupCell = false;
        }

        // Register the method in ThreadLocalRegistry with its original function pointer
        int methodKey = ThreadLocalRegistry.RegisterMethod(originalCodeAddr);

        // Generate the dispatcher
        var dispatcherMethod = DispatcherGenerator.GenerateDispatcher((MethodInfo)original, methodKey);
        RuntimeHelpers.PrepareMethod(dispatcherMethod.MethodHandle);
        IntPtr dispatcherFuncPtr = dispatcherMethod.MethodHandle.GetFunctionPointer();
        IntPtr dispatcherCodeAddr = ResolveJitCodeAddress(dispatcherFuncPtr);

        // Patch the original to redirect to the dispatcher
        byte[] savedBytes;
        IntPtr patchAddress;
        int patchedSize;

        if (isFixupCell)
        {
            // Fixup cell: swap pointer to dispatcher's function pointer
            MakeMemoryWritable(fixupCell);
            savedBytes = new byte[8];
            Marshal.Copy(fixupCell, savedBytes, 0, 8);
            byte[] newValue = BitConverter.GetBytes(dispatcherFuncPtr.ToInt64());
            Marshal.Copy(newValue, 0, fixupCell, 8);
            patchAddress = fixupCell;
            patchedSize = 8;
        }
        else
        {
            // JIT code: write JMP trampoline to dispatcher
            IntPtr codeAddr = ResolveJitCodeAddress(originalFuncPtr);
            MakeMemoryWritable(codeAddr);
            byte[] jmpBytes = GenerateJmpTrampoline(dispatcherCodeAddr);
            savedBytes = new byte[jmpBytes.Length];
            Marshal.Copy(codeAddr, savedBytes, 0, jmpBytes.Length);
            Marshal.Copy(jmpBytes, 0, codeAddr, jmpBytes.Length);
            FlushCache(codeAddr, jmpBytes.Length);
            patchAddress = codeAddr;
            patchedSize = jmpBytes.Length;
        }

        var info = new PatchedMethodInfo
        {
            MethodKey = methodKey,
            PatchAddress = patchAddress,
            SavedBytes = savedBytes,
            PatchSize = patchedSize,
            IsFixupCellPatch = isFixupCell,
            TrampolineAddress = trampolineAddr,
        };

        s_patchedMethods[original.MethodHandle] = info;
        return info;
    }

    private static void RestoreOriginalBytes(PatchedMethodInfo info)
    {
        MakeMemoryWritable(info.PatchAddress);
        Marshal.Copy(info.SavedBytes, 0, info.PatchAddress, info.PatchSize);

        if (!info.IsFixupCellPatch)
        {
            FlushCache(info.PatchAddress, info.PatchSize);
        }

        if (info.TrampolineAddress != IntPtr.Zero)
        {
            TrampolineAllocator.FreeTrampoline(info.TrampolineAddress);
        }
    }

    private static int GetPatchSize()
    {
        var arch = RuntimeInformation.ProcessArchitecture;
        if (arch == Architecture.X64) return 12;
        if (arch == Architecture.Arm64) return 20;
        if (arch == Architecture.X86) return 6;
        throw new PlatformNotSupportedException($"Architecture {arch} is not supported.");
    }

    /// <summary>
    /// Finds the fixup cell address from a precode.
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
                int disp = BitConverter.ToInt32(header, 2);
                return new IntPtr(funcPtr.ToInt64() + 6 + disp);
            }
        }
        else if (arch == Architecture.Arm64)
        {
            byte[] precodeBytes = new byte[12];
            Marshal.Copy(funcPtr, precodeBytes, 0, 12);

            uint instr1 = BitConverter.ToUInt32(precodeBytes, 0);
            uint instr2 = BitConverter.ToUInt32(precodeBytes, 4);
            uint instr3 = BitConverter.ToUInt32(precodeBytes, 8);

            // Pattern 1: ADRP + LDR [Xn, #imm] + BR (far target)
            if ((instr1 & 0x9F000000) == 0x90000000 &&
                (instr2 & 0xFFC00000) == 0xF9400000 &&
                (instr3 & 0xFFFFFC00) == 0xD61F0000)
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

            // Pattern 2: LDR Xt, #label + BR Xt (FixupPrecode — near literal load)
            // LDR (literal) 64-bit: opc=01 011 0 00 imm19 Rt → (instr & 0xFF000000) == 0x58000000
            // BR Xn: 1101011 0000 11111 000000 Rn 00000 → (instr & 0xFFFFFC1F) == 0xD61F0000
            if ((instr1 & 0xFF000000) == 0x58000000 &&
                (instr2 & 0xFFFFFC1F) == 0xD61F0000)
            {
                uint rt = instr1 & 0x1F;
                uint rn = (instr2 >> 5) & 0x1F;
                if (rt == rn)
                {
                    int imm19 = (int)((instr1 >> 5) & 0x7FFFF);
                    if ((imm19 & 0x40000) != 0)
                        imm19 |= unchecked((int)0xFFF80000); // sign-extend
                    long offset = (long)imm19 * 4;
                    return new IntPtr(funcPtr.ToInt64() + offset);
                }
            }

            // Pattern 3: LDR Xt1, #label1 + LDR Xt2, #label2 + BR Xt2 (StubPrecode)
            // Two LDR literals followed by BR through the second one's register
            if ((instr1 & 0xFF000000) == 0x58000000 &&
                (instr2 & 0xFF000000) == 0x58000000 &&
                (instr3 & 0xFFFFFC1F) == 0xD61F0000)
            {
                uint rt2 = instr2 & 0x1F;
                uint rn = (instr3 >> 5) & 0x1F;
                if (rt2 == rn)
                {
                    // The target cell is pointed to by the second LDR (the one BR uses)
                    int imm19 = (int)((instr2 >> 5) & 0x7FFFF);
                    if ((imm19 & 0x40000) != 0)
                        imm19 |= unchecked((int)0xFFF80000);
                    long offset = (long)imm19 * 4;
                    // PC is the address of instr2, which is funcPtr + 4
                    return new IntPtr(funcPtr.ToInt64() + 4 + offset);
                }
            }
        }

        return IntPtr.Zero;
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
        else if (arch == Architecture.Arm64)
        {
            byte[] precodeBytes = new byte[12];
            Marshal.Copy(funcPtr, precodeBytes, 0, 12);

            uint instr1 = BitConverter.ToUInt32(precodeBytes, 0);
            uint instr2 = BitConverter.ToUInt32(precodeBytes, 4);
            uint instr3 = BitConverter.ToUInt32(precodeBytes, 8);

            // LDR Xt, #label + BR Xt (FixupPrecode)
            if ((instr1 & 0xFF000000) == 0x58000000 &&
                (instr2 & 0xFFFFFC1F) == 0xD61F0000)
            {
                uint rt = instr1 & 0x1F;
                uint rn = (instr2 >> 5) & 0x1F;
                if (rt == rn)
                {
                    int imm19 = (int)((instr1 >> 5) & 0x7FFFF);
                    if ((imm19 & 0x40000) != 0)
                        imm19 |= unchecked((int)0xFFF80000);
                    long offset = (long)imm19 * 4;
                    IntPtr cell = new IntPtr(funcPtr.ToInt64() + offset);
                    byte[] targetBytes = new byte[8];
                    Marshal.Copy(cell, targetBytes, 0, 8);
                    return new IntPtr(BitConverter.ToInt64(targetBytes, 0));
                }
            }

            // LDR + LDR + BR (StubPrecode) — follow the second LDR's target
            if ((instr1 & 0xFF000000) == 0x58000000 &&
                (instr2 & 0xFF000000) == 0x58000000 &&
                (instr3 & 0xFFFFFC1F) == 0xD61F0000)
            {
                uint rt2 = instr2 & 0x1F;
                uint rn = (instr3 >> 5) & 0x1F;
                if (rt2 == rn)
                {
                    int imm19 = (int)((instr2 >> 5) & 0x7FFFF);
                    if ((imm19 & 0x40000) != 0)
                        imm19 |= unchecked((int)0xFFF80000);
                    long offset = (long)imm19 * 4;
                    IntPtr cell = new IntPtr(funcPtr.ToInt64() + 4 + offset);
                    byte[] targetBytes = new byte[8];
                    Marshal.Copy(cell, targetBytes, 0, 8);
                    return new IntPtr(BitConverter.ToInt64(targetBytes, 0));
                }
            }

            // ADRP + LDR + BR — follow the loaded value
            if ((instr1 & 0x9F000000) == 0x90000000 &&
                (instr2 & 0xFFC00000) == 0xF9400000 &&
                (instr3 & 0xFFFFFC00) == 0xD61F0000)
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

                IntPtr cell = new IntPtr(targetPage + ldrOffset);
                byte[] targetBytes = new byte[8];
                Marshal.Copy(cell, targetBytes, 0, 8);
                return new IntPtr(BitConverter.ToInt64(targetBytes, 0));
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

        throw new PlatformNotSupportedException(
            $"Architecture {arch} is not supported by InjectorPP.Net.");
    }
}

/// <summary>
/// Tracks a globally-patched method's state.
/// </summary>
internal class PatchedMethodInfo
{
    public int MethodKey { get; init; }
    public IntPtr PatchAddress { get; init; }
    public byte[] SavedBytes { get; init; } = null!;
    public int PatchSize { get; init; }
    public bool IsFixupCellPatch { get; init; }
    public IntPtr TrampolineAddress { get; init; }
}

/// <summary>
/// Stores information needed to undo a single thread's method replacement.
/// </summary>
internal sealed class MethodReplacement
{
    public MethodBase OriginalMethod { get; init; } = null!;
    public int MethodKey { get; init; }
}
