using System;
using System.Collections.Generic;
using libdebug;

namespace PS4GSCInjector.GameProfiles
{
    public static class MemoryScriptPointerLocator
    {
        private const int T8HeaderSize = 0x60;
        private const int ScriptNameOffset = 0x10;
        private const int T8ScriptParseTreeEntrySize = 0x20;
        private const int T8ScriptParseTreeBufferOffset = 0x10;
        private const int T8ScriptParseTreeSizeOffset = 0x18;
        private const int ReadChunkSize = 1024 * 1024;
        private static readonly byte[] T8Magic = { 0x80, 0x47, 0x53, 0x43, 0x0D, 0x0A, 0x00, 0x36 };

        public static bool TryFindT8ScriptParseTreeEntries(
            PS4DBG ps4,
            libdebug.Process process,
            ulong surrogateScriptHash,
            ulong targetScriptHash,
            out T8ScriptParseTreeEntry surrogateEntry,
            out T8ScriptParseTreeEntry targetEntry)
        {
            surrogateEntry = null;
            targetEntry = null;

            if (ps4 == null || process == null || surrogateScriptHash == 0 || targetScriptHash == 0)
                return false;

            ProcessMap processMap = ps4.GetProcessMaps(process.pid);
            if (processMap?.entries == null)
                return false;

            foreach (var entry in processMap.entries)
            {
                if (!IsWritable(entry))
                    continue;

                foreach (var candidate in ScanEntry(ps4, process.pid, entry, T8ScriptParseTreeEntrySize, memory =>
                    FindT8ScriptParseTreeEntries(memory.Buffer, memory.BaseAddress, surrogateScriptHash, targetScriptHash)))
                {
                    if (!IsValidT8ScriptBuffer(ps4, process.pid, candidate.BufferAddress, candidate.ScriptName))
                        continue;

                    if (candidate.ScriptName == surrogateScriptHash)
                        surrogateEntry = candidate;
                    else if (candidate.ScriptName == targetScriptHash)
                        targetEntry = candidate;

                    if (surrogateEntry != null && targetEntry != null)
                        return true;
                }
            }

            return false;
        }

        public static IEnumerable<T8ScriptParseTreeEntry> FindT8ScriptParseTreeEntries(
            byte[] memory,
            ulong baseAddress,
            ulong surrogateScriptHash,
            ulong targetScriptHash)
        {
            if (memory == null || memory.Length < T8ScriptParseTreeEntrySize)
                yield break;

            for (var index = 0; index <= memory.Length - T8ScriptParseTreeEntrySize; index += sizeof(ulong))
            {
                ulong scriptName = BitConverter.ToUInt64(memory, index);
                if (scriptName != surrogateScriptHash && scriptName != targetScriptHash)
                    continue;

                ulong bufferAddress = BitConverter.ToUInt64(memory, index + T8ScriptParseTreeBufferOffset);
                int size = BitConverter.ToInt32(memory, index + T8ScriptParseTreeSizeOffset);
                if (bufferAddress == 0 || size <= T8HeaderSize || size > 16 * 1024 * 1024)
                    continue;

                yield return new T8ScriptParseTreeEntry(
                    baseAddress + (ulong)index,
                    scriptName,
                    bufferAddress,
                    size);
            }
        }

        private static IEnumerable<T> ScanEntry<T>(
            PS4DBG ps4,
            int processId,
            MemoryEntry entry,
            int overlapSize,
            Func<MemoryChunk, IEnumerable<T>> scanChunk)
        {
            if (!IsReadable(entry) || entry.end <= entry.start)
                yield break;

            ulong entrySize = entry.end - entry.start;
            ulong offset = 0;
            int overlap = Math.Max(0, overlapSize - 1);

            while (offset < entrySize)
            {
                int readSize = GetReadSize(entrySize - offset, ReadChunkSize, overlap);

                byte[] buffer;
                var chunkAddress = entry.start + offset;
                try
                {
                    buffer = ps4.ReadMemory(processId, chunkAddress, readSize);
                }
                catch
                {
                    yield break;
                }

                foreach (var address in scanChunk(new MemoryChunk(buffer, chunkAddress)))
                    yield return address;

                offset += (ulong)Math.Min(ReadChunkSize, readSize);
            }
        }

        private static bool IsReadable(MemoryEntry entry)
        {
            return entry != null && (entry.prot & (uint)PS4DBG.VM_PROTECTIONS.VM_PROT_READ) != 0;
        }

        internal static int GetReadSize(ulong remaining, int chunkSize, int overlap)
        {
            return (int)Math.Min((ulong)checked(chunkSize + overlap), remaining);
        }

        private static bool IsWritable(MemoryEntry entry)
        {
            return entry != null && (entry.prot & (uint)PS4DBG.VM_PROTECTIONS.VM_PROT_WRITE) != 0;
        }

        private static bool IsValidT8ScriptBuffer(PS4DBG ps4, int processId, ulong bufferAddress, ulong scriptNameHash)
        {
            try
            {
                var header = ps4.ReadMemory(processId, bufferAddress, T8HeaderSize);
                return IsValidT8ScriptHeader(header, scriptNameHash);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsValidT8ScriptHeader(byte[] header, ulong scriptNameHash)
        {
            return header != null &&
                header.Length >= T8HeaderSize &&
                MatchesAt(header, 0, T8Magic) &&
                BitConverter.ToUInt64(header, ScriptNameOffset) == scriptNameHash;
        }

        private static bool MatchesAt(byte[] buffer, int offset, byte[] pattern)
        {
            for (var index = 0; index < pattern.Length; index++)
            {
                if (buffer[offset + index] != pattern[index])
                    return false;
            }

            return true;
        }

        private sealed class MemoryChunk
        {
            public MemoryChunk(byte[] buffer, ulong baseAddress)
            {
                Buffer = buffer;
                BaseAddress = baseAddress;
            }

            public byte[] Buffer { get; }

            public ulong BaseAddress { get; }
        }

        public sealed class T8ScriptParseTreeEntry
        {
            public T8ScriptParseTreeEntry(ulong entryAddress, ulong scriptName, ulong bufferAddress, int size)
            {
                EntryAddress = entryAddress;
                ScriptName = scriptName;
                BufferAddress = bufferAddress;
                Size = size;
            }

            public ulong EntryAddress { get; }

            public ulong ScriptName { get; }

            public ulong BufferAddress { get; }

            public int Size { get; }
        }
    }
}
