using System.Runtime.InteropServices;

namespace InjectorPP.Net;

/// <summary>
/// Minimal x64/ARM64/x86 instruction length decoder for creating trampolines.
/// Determines how many bytes of complete instructions need to be copied to cover
/// the JMP trampoline patch size.
/// </summary>
internal static class InstructionDecoder
{
    /// <summary>
    /// Calculates the minimum number of bytes of complete instructions
    /// that must be copied to cover at least <paramref name="minBytes"/>.
    /// Also checks for PC-relative instructions that would break if relocated.
    /// </summary>
    public static int GetCopySize(IntPtr address, int minBytes)
    {
        var arch = RuntimeInformation.ProcessArchitecture;

        if (arch == Architecture.Arm64)
        {
            // ARM64: every instruction is exactly 4 bytes
            int count = (minBytes + 3) / 4 * 4;
            // Check for PC-relative instructions
            CheckArm64PCRelative(address, count);
            return count;
        }

        if (arch == Architecture.X86 || arch == Architecture.X64)
        {
            return GetX86X64CopySize(address, minBytes, arch == Architecture.X64);
        }

        throw new PlatformNotSupportedException(
            $"Instruction decoding not supported for {arch}.");
    }

    private static int GetX86X64CopySize(IntPtr address, int minBytes, bool is64Bit)
    {
        int offset = 0;
        byte[] code = new byte[minBytes + 32]; // Read extra for safety
        Marshal.Copy(address, code, 0, code.Length);

        while (offset < minBytes)
        {
            int instrLen = DecodeInstructionLength(code, offset, is64Bit);
            if (instrLen <= 0)
                throw new InvalidOperationException(
                    $"Failed to decode instruction at offset {offset} (byte: 0x{code[offset]:X2}). " +
                    "Thread-local dispatch is not supported for this method.");

            // Check for PC-relative instructions
            byte opcode = code[offset];
            // Skip prefixes to find the actual opcode
            int opcodeOffset = SkipPrefixes(code, offset, is64Bit);
            byte actualOpcode = code[opcodeOffset];

            if (IsPCRelativeOpcode(code, opcodeOffset, is64Bit))
            {
                throw new InvalidOperationException(
                    $"PC-relative instruction (0x{actualOpcode:X2}) found at offset {offset}. " +
                    "Thread-local dispatch trampoline cannot relocate this instruction.");
            }

            offset += instrLen;
        }

        return offset;
    }

    private static int SkipPrefixes(byte[] code, int offset, bool is64Bit)
    {
        int i = offset;
        while (i < code.Length)
        {
            byte b = code[i];
            // Legacy prefixes
            if (b == 0x66 || b == 0x67 || b == 0xF0 || b == 0xF2 || b == 0xF3 ||
                b == 0x26 || b == 0x2E || b == 0x36 || b == 0x3E || b == 0x64 || b == 0x65)
            {
                i++;
                continue;
            }
            // REX prefixes (x64 only)
            if (is64Bit && b >= 0x40 && b <= 0x4F)
            {
                i++;
                continue;
            }
            break;
        }
        return i;
    }

    private static bool IsPCRelativeOpcode(byte[] code, int opcodeOffset, bool is64Bit)
    {
        byte op = code[opcodeOffset];

        // E8: CALL rel32 (PC-relative)
        // E9: JMP rel32 (PC-relative)
        // EB: JMP rel8 (PC-relative)
        if (op == 0xE8 || op == 0xE9 || op == 0xEB)
            return true;

        // 70-7F: Jcc rel8 (PC-relative)
        if (op >= 0x70 && op <= 0x7F)
            return true;

        // E0-E3: LOOP/JECXZ (PC-relative)
        if (op >= 0xE0 && op <= 0xE3)
            return true;

        // 0F 80-8F: Jcc rel32 (PC-relative)
        if (op == 0x0F && opcodeOffset + 1 < code.Length)
        {
            byte op2 = code[opcodeOffset + 1];
            if (op2 >= 0x80 && op2 <= 0x8F)
                return true;
        }

        // Check for RIP-relative addressing (x64 only): ModR/M with mod=00, rm=101
        if (is64Bit && HasModRM(code, opcodeOffset))
        {
            int modrmOffset = GetModRMOffset(code, opcodeOffset);
            if (modrmOffset < code.Length)
            {
                byte modrm = code[modrmOffset];
                int mod = (modrm >> 6) & 3;
                int rm = modrm & 7;
                if (mod == 0 && rm == 5)
                    return true; // RIP-relative addressing
            }
        }

        return false;
    }

    private static bool HasModRM(byte[] code, int opcodeOffset)
    {
        byte op = code[opcodeOffset];

        // 2-byte opcode
        if (op == 0x0F)
            return true; // Most 2-byte opcodes have ModR/M

        // 1-byte opcodes with ModR/M
        if ((op & 0xFC) <= 0x3C && (op & 0x07) <= 0x03)
            return true; // ALU ops: 00-03, 08-0B, 10-13, 18-1B, 20-23, 28-2B, 30-33, 38-3B
        if (op >= 0x80 && op <= 0x8F)
            return true;
        if (op == 0x63 || op == 0x69 || op == 0x6B)
            return true;
        if (op >= 0x84 && op <= 0x8D)
            return true;
        if (op == 0xC0 || op == 0xC1 || op == 0xC6 || op == 0xC7)
            return true;
        if (op == 0xD0 || op == 0xD1 || op == 0xD2 || op == 0xD3)
            return true;
        if (op == 0xF6 || op == 0xF7 || op == 0xFE || op == 0xFF)
            return true;

        return false;
    }

    private static int GetModRMOffset(byte[] code, int opcodeOffset)
    {
        byte op = code[opcodeOffset];
        if (op == 0x0F)
            return opcodeOffset + 2; // After 2-byte opcode
        return opcodeOffset + 1; // After 1-byte opcode
    }

    /// <summary>
    /// Decodes the length of a single x86/x64 instruction.
    /// Returns -1 if the instruction cannot be decoded.
    /// </summary>
    private static int DecodeInstructionLength(byte[] code, int offset, bool is64Bit)
    {
        int start = offset;
        bool rexW = false;
        bool has66 = false;

        // Parse prefixes
        while (offset < code.Length)
        {
            byte b = code[offset];

            // Legacy prefixes
            if (b == 0x66) { has66 = true; offset++; continue; }
            if (b == 0x67 || b == 0xF0 || b == 0xF2 || b == 0xF3 ||
                b == 0x26 || b == 0x2E || b == 0x36 || b == 0x3E || b == 0x64 || b == 0x65)
            {
                offset++;
                continue;
            }

            // REX prefix (x64 only, 0x40-0x4F)
            if (is64Bit && b >= 0x40 && b <= 0x4F)
            {
                rexW = (b & 0x08) != 0;
                offset++;
                continue;
            }

            break;
        }

        if (offset >= code.Length) return -1;
        byte opcode = code[offset++];

        // 2-byte opcode (0x0F prefix)
        if (opcode == 0x0F)
        {
            if (offset >= code.Length) return -1;
            byte opcode2 = code[offset++];
            return offset - start + Decode2ByteOpcodeExtra(code, ref offset, opcode2);
        }

        // 1-byte opcodes
        int extra = Decode1ByteOpcodeExtra(code, ref offset, opcode, is64Bit, rexW, has66);
        if (extra < 0) return -1;
        return offset - start + extra;
    }

    private static int Decode1ByteOpcodeExtra(byte[] code, ref int offset, byte opcode,
        bool is64Bit, bool rexW, bool has66)
    {
        // NOP
        if (opcode == 0x90) return 0;

        // RET
        if (opcode == 0xC3) return 0;
        if (opcode == 0xC2) return 2; // RET imm16

        // PUSH/POP register (50-5F)
        if (opcode >= 0x50 && opcode <= 0x5F) return 0;

        // MOV r8, imm8 (B0-B7)
        if (opcode >= 0xB0 && opcode <= 0xB7) return 1;

        // MOV r, imm32/imm64 (B8-BF)
        if (opcode >= 0xB8 && opcode <= 0xBF)
            return (is64Bit && rexW) ? 8 : 4;

        // INT3
        if (opcode == 0xCC) return 0;

        // CALL rel32, JMP rel32
        if (opcode == 0xE8 || opcode == 0xE9) return 4;

        // JMP rel8
        if (opcode == 0xEB) return 1;

        // Jcc rel8 (70-7F)
        if (opcode >= 0x70 && opcode <= 0x7F) return 1;

        // LOOP/JECXZ (E0-E3)
        if (opcode >= 0xE0 && opcode <= 0xE3) return 1;

        // PUSH imm32
        if (opcode == 0x68) return 4;
        // PUSH imm8
        if (opcode == 0x6A) return 1;

        // CBW/CWDE/CDQE, CWD/CDQ/CQO
        if (opcode == 0x98 || opcode == 0x99) return 0;

        // XCHG r, rAX (91-97)
        if (opcode >= 0x91 && opcode <= 0x97) return 0;

        // ALU r/m, r or r, r/m (00-3F, pattern: low 3 bits determine direction/size)
        if (opcode <= 0x3F)
        {
            int low3 = opcode & 0x07;
            if (low3 <= 3)
            {
                // ModR/M forms
                return DecodeModRM(code, ref offset, is64Bit);
            }
            if (low3 == 4) return 1; // AL, imm8
            if (low3 == 5) return has66 ? 2 : 4; // rAX, imm32/16
            // 06, 07, 0E, 0F etc are segment prefix or 2-byte escape
            return 0;
        }

        // MOVSXD (63)
        if (opcode == 0x63)
            return DecodeModRM(code, ref offset, is64Bit);

        // IMUL r, r/m, imm32 (69) or imm8 (6B)
        if (opcode == 0x69)
            return DecodeModRM(code, ref offset, is64Bit) + (has66 ? 2 : 4);
        if (opcode == 0x6B)
            return DecodeModRM(code, ref offset, is64Bit) + 1;

        // Immediate group (80-83)
        if (opcode == 0x80) return DecodeModRM(code, ref offset, is64Bit) + 1;
        if (opcode == 0x81) return DecodeModRM(code, ref offset, is64Bit) + (has66 ? 2 : 4);
        if (opcode == 0x83) return DecodeModRM(code, ref offset, is64Bit) + 1;

        // TEST, XCHG, MOV (84-8D)
        if (opcode >= 0x84 && opcode <= 0x8D)
            return DecodeModRM(code, ref offset, is64Bit);

        // MOV moffs (A0-A3)
        if (opcode >= 0xA0 && opcode <= 0xA3)
            return is64Bit ? 8 : 4;

        // TEST AL/rAX, imm
        if (opcode == 0xA8) return 1;
        if (opcode == 0xA9) return has66 ? 2 : 4;

        // Shift group (C0, C1)
        if (opcode == 0xC0) return DecodeModRM(code, ref offset, is64Bit) + 1;
        if (opcode == 0xC1) return DecodeModRM(code, ref offset, is64Bit) + 1;

        // MOV r/m, imm (C6, C7)
        if (opcode == 0xC6) return DecodeModRM(code, ref offset, is64Bit) + 1;
        if (opcode == 0xC7) return DecodeModRM(code, ref offset, is64Bit) + (has66 ? 2 : 4);

        // Shift group (D0-D3)
        if (opcode >= 0xD0 && opcode <= 0xD3)
            return DecodeModRM(code, ref offset, is64Bit);

        // TEST/NOT/NEG/MUL/IMUL/DIV/IDIV (F6, F7)
        if (opcode == 0xF6)
        {
            int modrm = DecodeModRM(code, ref offset, is64Bit);
            int reg = (code[offset - modrm] >> 3) & 7; // Re-read ModR/M
            // reg=0 (TEST) has imm8
            return modrm + (reg == 0 ? 1 : 0);
        }
        if (opcode == 0xF7)
        {
            // Need to peek at ModR/M to determine reg field
            if (offset >= code.Length) return -1;
            int reg = (code[offset] >> 3) & 7;
            int modrm = DecodeModRM(code, ref offset, is64Bit);
            // reg=0 (TEST) has imm32
            return modrm + (reg == 0 ? (has66 ? 2 : 4) : 0);
        }

        // INC/DEC (FE)
        if (opcode == 0xFE)
            return DecodeModRM(code, ref offset, is64Bit);

        // INC/DEC/CALL/JMP/PUSH (FF)
        if (opcode == 0xFF)
            return DecodeModRM(code, ref offset, is64Bit);

        // LEAVE
        if (opcode == 0xC9) return 0;

        // CLD/STD
        if (opcode == 0xFC || opcode == 0xFD) return 0;

        // SAHF/LAHF
        if (opcode == 0x9E || opcode == 0x9F) return 0;

        return -1; // Unknown opcode
    }

    private static int Decode2ByteOpcodeExtra(byte[] code, ref int offset, byte opcode2)
    {
        // Jcc rel32 (80-8F)
        if (opcode2 >= 0x80 && opcode2 <= 0x8F) return 4;

        // SETcc (90-9F) - ModR/M
        if (opcode2 >= 0x90 && opcode2 <= 0x9F)
            return DecodeModRM(code, ref offset, true);

        // CMOVcc (40-4F) - ModR/M
        if (opcode2 >= 0x40 && opcode2 <= 0x4F)
            return DecodeModRM(code, ref offset, true);

        // MOVZX, MOVSX (B6, B7, BE, BF) - ModR/M
        if (opcode2 == 0xB6 || opcode2 == 0xB7 || opcode2 == 0xBE || opcode2 == 0xBF)
            return DecodeModRM(code, ref offset, true);

        // NOP (1F) - ModR/M (multi-byte NOP)
        if (opcode2 == 0x1F)
            return DecodeModRM(code, ref offset, true);

        // BSR/BSF (BC, BD)
        if (opcode2 == 0xBC || opcode2 == 0xBD)
            return DecodeModRM(code, ref offset, true);

        // IMUL r, r/m (AF)
        if (opcode2 == 0xAF)
            return DecodeModRM(code, ref offset, true);

        // BT/BTS/BTR/BTC (A3, AB, B3, BB)
        if (opcode2 == 0xA3 || opcode2 == 0xAB || opcode2 == 0xB3 || opcode2 == 0xBB)
            return DecodeModRM(code, ref offset, true);

        // XADD (C0, C1)
        if (opcode2 == 0xC0 || opcode2 == 0xC1)
            return DecodeModRM(code, ref offset, true);

        // CMPXCHG (B0, B1)
        if (opcode2 == 0xB0 || opcode2 == 0xB1)
            return DecodeModRM(code, ref offset, true);

        // MOVNTI, MOVD, etc - ModR/M
        if (opcode2 >= 0x10 && opcode2 <= 0x1E)
            return DecodeModRM(code, ref offset, true);

        return -1; // Unknown 2-byte opcode
    }

    /// <summary>
    /// Decodes the ModR/M byte (and optional SIB + displacement).
    /// Returns the total extra bytes consumed (ModR/M + SIB + displacement).
    /// Advances offset past these bytes.
    /// </summary>
    private static int DecodeModRM(byte[] code, ref int offset, bool is64Bit)
    {
        if (offset >= code.Length) return 0;

        byte modrm = code[offset++];
        int mod = (modrm >> 6) & 3;
        int rm = modrm & 7;
        int extra = 1; // ModR/M byte itself

        if (mod == 3)
            return extra; // Register-to-register, no SIB or displacement

        // Check for SIB byte
        bool hasSIB = (rm == 4);
        if (hasSIB && offset < code.Length)
        {
            byte sib = code[offset++];
            extra++;
            int sibBase = sib & 7;

            if (mod == 0 && sibBase == 5)
            {
                offset += 4;
                extra += 4; // disp32
            }
        }

        // Displacement
        if (mod == 0)
        {
            if (!hasSIB && rm == 5)
            {
                // RIP-relative (x64) or disp32 (x86)
                offset += 4;
                extra += 4;
            }
            // Otherwise no displacement (unless SIB handled above)
        }
        else if (mod == 1)
        {
            offset += 1;
            extra += 1; // disp8
        }
        else if (mod == 2)
        {
            offset += 4;
            extra += 4; // disp32
        }

        return extra;
    }

    private static void CheckArm64PCRelative(IntPtr address, int byteCount)
    {
        byte[] code = new byte[byteCount];
        Marshal.Copy(address, code, 0, byteCount);

        for (int i = 0; i < byteCount; i += 4)
        {
            uint instr = BitConverter.ToUInt32(code, i);

            // ADRP (1xx10000)
            if ((instr & 0x9F000000) == 0x90000000)
                throw new InvalidOperationException(
                    $"PC-relative ADRP instruction found at offset {i}. " +
                    "Thread-local dispatch trampoline cannot relocate this instruction.");

            // ADR (0xx10000)
            if ((instr & 0x9F000000) == 0x10000000)
                throw new InvalidOperationException(
                    $"PC-relative ADR instruction found at offset {i}.");

            // B/BL (x00101xx)
            if ((instr & 0x7C000000) == 0x14000000)
                throw new InvalidOperationException(
                    $"PC-relative B/BL instruction found at offset {i}.");

            // B.cond (01010100)
            if ((instr & 0xFF000010) == 0x54000000)
                throw new InvalidOperationException(
                    $"PC-relative B.cond instruction found at offset {i}.");

            // CBZ/CBNZ (x0110100 / x0110101)
            if ((instr & 0x7E000000) == 0x34000000)
                throw new InvalidOperationException(
                    $"PC-relative CBZ/CBNZ instruction found at offset {i}.");

            // TBZ/TBNZ (x0110110 / x0110111)
            if ((instr & 0x7E000000) == 0x36000000)
                throw new InvalidOperationException(
                    $"PC-relative TBZ/TBNZ instruction found at offset {i}.");

            // LDR literal (0x18/0x1C prefix pattern)
            if ((instr & 0x3B000000) == 0x18000000)
                throw new InvalidOperationException(
                    $"PC-relative LDR literal instruction found at offset {i}.");
        }
    }
}
