using System.Runtime.InteropServices;

namespace InjectorPP.Net;

/// <summary>
/// Allocates executable memory for trampolines that call the original method.
/// A trampoline contains the original method's overwritten prologue bytes
/// followed by a JMP back to the original method past the patched region.
/// </summary>
internal static class TrampolineAllocator
{
    // Windows
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFree(IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);

    // Linux
    [DllImport("libc", EntryPoint = "mmap", SetLastError = true)]
    private static extern IntPtr LinuxMMap(IntPtr addr, UIntPtr length, int prot, int flags, int fd, long offset);

    [DllImport("libc", EntryPoint = "munmap", SetLastError = true)]
    private static extern int LinuxMUnmap(IntPtr addr, UIntPtr length);

    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RESERVE = 0x2000;
    private const uint MEM_RELEASE = 0x8000;
    private const uint PAGE_EXECUTE_READWRITE = 0x40;

    // Linux mmap constants
    private const int PROT_READ = 1;
    private const int PROT_WRITE = 2;
    private const int PROT_EXEC = 4;
    private const int MAP_PRIVATE = 0x02;
    private const int MAP_ANONYMOUS = 0x20;

    /// <summary>
    /// Creates a trampoline that executes the original method's overwritten bytes
    /// and then jumps back to the original method past the patch region.
    /// </summary>
    /// <param name="originalAddress">Address of the original method's JIT code.</param>
    /// <param name="patchSize">Size of the patch (number of bytes overwritten).</param>
    /// <returns>Function pointer to the trampoline.</returns>
    public static IntPtr CreateTrampoline(IntPtr originalAddress, int patchSize)
    {
        // Determine how many complete instructions to copy
        int copySize = InstructionDecoder.GetCopySize(originalAddress, patchSize);

        // Read the original bytes before they're overwritten
        byte[] originalBytes = new byte[copySize];
        Marshal.Copy(originalAddress, originalBytes, 0, copySize);

        // Generate the JMP back instruction
        IntPtr returnAddress = new IntPtr(originalAddress.ToInt64() + copySize);
        byte[] jmpBack = GenerateJmpTrampoline(returnAddress);

        // Allocate executable memory: original bytes + JMP back
        int totalSize = copySize + jmpBack.Length;
        IntPtr trampoline = AllocateExecutableMemory(totalSize);

        // Write: [original bytes] + [JMP back to original + copySize]
        Marshal.Copy(originalBytes, 0, trampoline, copySize);
        Marshal.Copy(jmpBack, 0, trampoline + copySize, jmpBack.Length);

        FlushInstructionCache(trampoline, totalSize);

        return trampoline;
    }

    /// <summary>
    /// Frees trampoline memory.
    /// </summary>
    public static void FreeTrampoline(IntPtr trampolineAddress)
    {
        if (trampolineAddress == IntPtr.Zero) return;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            VirtualFree(trampolineAddress, UIntPtr.Zero, MEM_RELEASE);
        }
        else
        {
            // Use page size as the allocation size (same as what we allocated)
            LinuxMUnmap(trampolineAddress, new UIntPtr((uint)Environment.SystemPageSize));
        }
    }

    private static IntPtr AllocateExecutableMemory(int size)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            IntPtr mem = VirtualAlloc(IntPtr.Zero, new UIntPtr((uint)size),
                MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
            if (mem == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"VirtualAlloc failed. Error: {Marshal.GetLastWin32Error()}");
            return mem;
        }
        else
        {
            IntPtr mem = LinuxMMap(IntPtr.Zero, new UIntPtr((uint)size),
                PROT_READ | PROT_WRITE | PROT_EXEC,
                MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
            if (mem == new IntPtr(-1))
                throw new InvalidOperationException(
                    $"mmap failed. Error: {Marshal.GetLastWin32Error()}");
            return mem;
        }
    }

    private static byte[] GenerateJmpTrampoline(IntPtr target)
    {
        var arch = RuntimeInformation.ProcessArchitecture;

        if (arch == Architecture.X64)
        {
            byte[] jmp = new byte[12];
            jmp[0] = 0x48; jmp[1] = 0xB8; // MOVABS RAX
            BitConverter.GetBytes(target.ToInt64()).CopyTo(jmp, 2);
            jmp[10] = 0xFF; jmp[11] = 0xE0; // JMP RAX
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
            jmp[0] = 0x68; // PUSH
            BitConverter.GetBytes(target.ToInt32()).CopyTo(jmp, 1);
            jmp[5] = 0xC3; // RET
            return jmp;
        }

        throw new PlatformNotSupportedException($"Architecture {arch} is not supported.");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FlushInstructionCache(IntPtr hProcess, IntPtr lpBaseAddress, UIntPtr dwSize);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    private static void FlushInstructionCache(IntPtr address, int size)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            FlushInstructionCache(GetCurrentProcess(), address, new UIntPtr((uint)size));
        }
    }
}
