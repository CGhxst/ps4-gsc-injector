using System;

namespace PS4GSCInjector.GameProfiles
{
    public static class TreyarchCompiledScriptValidator
    {
        private static readonly byte[] T6Magic = { 0x80, 0x47, 0x53, 0x43, 0x0D, 0x0A, 0x00, 0x06 };
        private static readonly byte[] T7Magic = { 0x80, 0x47, 0x53, 0x43, 0x0D, 0x0A, 0x00, 0x1C };
        private static readonly byte[] T8Magic = { 0x80, 0x47, 0x53, 0x43, 0x0D, 0x0A, 0x00, 0x36 };

        public static bool IsValid(byte[] script, CompiledScriptFormat format)
        {
            return IsValid(script, format, false);
        }

        internal static bool IsValid(byte[] script, CompiledScriptFormat format, bool headerOnly)
        {
            int minimumLength;
            byte[] expectedMagic;
            switch (format)
            {
                case CompiledScriptFormat.T6:
                    minimumLength = 0x40;
                    expectedMagic = T6Magic;
                    break;
                case CompiledScriptFormat.T7:
                    minimumLength = 0x50;
                    expectedMagic = T7Magic;
                    break;
                case CompiledScriptFormat.T8:
                    minimumLength = 0x60;
                    expectedMagic = T8Magic;
                    break;
                default:
                    return false;
            }

            if (script == null || script.Length < minimumLength)
                return false;

            for (var index = 0; index < expectedMagic.Length; index++)
            {
                if (script[index] != expectedMagic[index])
                    return false;
            }

            if (headerOnly) return true;
            if (script.Length <= minimumLength || script.Length > 16 * 1024 * 1024) return false;
            if (format == CompiledScriptFormat.T6) return ValidateT6(script);

            bool t8 = format == CompiledScriptFormat.T8;
            int header = t8 ? 0x60 : 0x50;
            uint includeOffset = BitConverter.ToUInt32(script, t8 ? 0x18 : 0x0C);
            int includes = t8 ? BitConverter.ToUInt16(script, 0x58) : script[0x42];
            if (!Range(script, includeOffset, (ulong)includes * (t8 ? 8u : 4u), header)) return false;
            if (!t8)
            {
                if (!TerminatedString(script, BitConverter.ToUInt32(script, 0x34), header)) return false;
                if (!Range(script, BitConverter.ToUInt32(script, 0x14), BitConverter.ToUInt32(script, 0x30), header)) return false;
                for (int i = 0; i < includes; i++)
                    if (!TerminatedString(script, BitConverter.ToUInt32(script, (int)includeOffset + i * 4), header)) return false;
            }

            uint exports = BitConverter.ToUInt32(script, t8 ? 0x30 : 0x20);
            int count = BitConverter.ToUInt16(script, t8 ? 0x1E : 0x3A);
            int exportSize = t8 ? 24 : 20;
            if (!Range(script, exports, (ulong)count * (uint)exportSize, header)) return false;
            for (int i = 0; i < count; i++)
                if (!Range(script, BitConverter.ToUInt32(script, (int)exports + i * exportSize + 4), 2, header)) return false;

            return ReferenceTable(script, BitConverter.ToUInt32(script, t8 ? 0x38 : 0x24),
                       BitConverter.ToUInt16(script, t8 ? 0x28 : 0x3C), false, header) &&
                   ReferenceTable(script, BitConverter.ToUInt32(script, t8 ? 0x24 : 0x18),
                       BitConverter.ToUInt16(script, t8 ? 0x1C : 0x38), true, header);
        }

        private static bool Range(byte[] script, ulong offset, ulong size, int header)
        {
            if (size == 0) return offset == 0 || (offset >= (ulong)header && offset <= (ulong)script.Length);
            return offset >= (ulong)header && offset <= (ulong)script.Length && size <= (ulong)script.Length - offset;
        }

        private static bool ValidateT6(byte[] script)
        {
            const int header = 0x40;
            foreach (int field in new[] { 0x0C, 0x10, 0x14, 0x18, 0x1C, 0x20, 0x24, 0x28 })
                if (!Range(script, BitConverter.ToUInt32(script, field), 0, header)) return false;
            if (!Range(script, BitConverter.ToUInt32(script, 0x14), BitConverter.ToUInt32(script, 0x2C), header) ||
                !TerminatedString(script, BitConverter.ToUInt16(script, 0x30), header)) return false;

            uint includes = BitConverter.ToUInt32(script, 0x0C);
            if (!Range(script, includes, (ulong)script[0x3C] * 4, header)) return false;
            for (int i = 0; i < script[0x3C]; i++)
                if (!TerminatedString(script, BitConverter.ToUInt32(script, (int)includes + i * 4), header)) return false;

            uint exports = BitConverter.ToUInt32(script, 0x1C);
            int exportCount = BitConverter.ToUInt16(script, 0x34);
            if (!Range(script, exports, (ulong)exportCount * 12, header)) return false;
            for (int i = 0; i < exportCount; i++)
            {
                int p = (int)exports + i * 12;
                if (!Range(script, BitConverter.ToUInt32(script, p + 4), 1, header) ||
                    !TerminatedString(script, BitConverter.ToUInt16(script, p + 8), header)) return false;
            }
            return T6References(script, 0x20, BitConverter.ToUInt16(script, 0x36), true) &&
                   T6References(script, 0x18, BitConverter.ToUInt16(script, 0x32), false);
        }

        private static bool T6References(byte[] script, int offsetField, int count, bool imports)
        {
            ulong position = BitConverter.ToUInt32(script, offsetField);
            int entrySize = imports ? 8 : 4;
            for (int i = 0; i < count; i++)
            {
                if (!Range(script, position, (ulong)entrySize, 0x40)) return false;
                int p = (int)position;
                if (!TerminatedString(script, BitConverter.ToUInt16(script, p), 0x40)) return false;
                if (imports && !TerminatedString(script, BitConverter.ToUInt16(script, p + 2), 0x40)) return false;
                int references = imports ? BitConverter.ToUInt16(script, p + 4) : script[p + 2];
                position += (ulong)entrySize;
                if (!Range(script, position, (ulong)references * 4, 0x40)) return false;
                for (int j = 0; j < references; j++)
                    if (!Range(script, BitConverter.ToUInt32(script, (int)position + j * 4), 1, 0x40)) return false;
                position += (ulong)references * 4;
            }
            return true;
        }

        private static bool TerminatedString(byte[] script, uint offset, int header)
        {
            return Range(script, offset, 1, header) && Array.IndexOf(script, (byte)0, (int)offset) >= 0;
        }

        private static bool ReferenceTable(byte[] script, uint start, int count, bool strings, int header)
        {
            ulong position = start;
            int entrySize = strings ? 8 : 12;
            if (!Range(script, position, (ulong)count * (uint)entrySize, header)) return false;
            for (int i = 0; i < count; i++)
            {
                if (!Range(script, position, (ulong)entrySize, header)) return false;
                int p = (int)position;
                if (strings && !TerminatedString(script, BitConverter.ToUInt32(script, p), header)) return false;
                int references = strings ? script[p + 4] : BitConverter.ToUInt16(script, p + 8);
                position += (ulong)entrySize;
                if (!Range(script, position, (ulong)references * 4, header)) return false;
                for (int j = 0; j < references; j++)
                    if (!Range(script, BitConverter.ToUInt32(script, (int)position + j * 4), 2, header)) return false;
                position += (ulong)references * 4;
            }
            return true;
        }
    }
}
