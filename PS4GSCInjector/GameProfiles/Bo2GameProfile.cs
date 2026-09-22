using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using libdebug;
using TreyarchCompiler.Utilities;

namespace PS4GSCInjector.GameProfiles
{
    public sealed class Bo2GameProfile : IGscGameProfile
    {
        private const uint ScriptParseTreeAssetType = 49;
        private const int ScriptHeaderSize = 0x40;
        private const int ScriptRecordSize = 0x18;
        private const int ScanChunkSize = 256 * 1024;
        private const int ScanOverlap = 128;

        private static readonly byte[] T6Magic =
        {
            0x80, 0x47, 0x53, 0x43, 0x0D, 0x0A, 0x00, 0x06
        };

        private static readonly IReadOnlyList<string> Symbols =
            new[] { "BO2", "T6", "PS4", "__PS4", "_GSC" };

        private static readonly IReadOnlyList<GameVersionProfile> GameVersions = new[]
        {
            new GameVersionProfile(
                "bo2-1.10-mp",
                "1.10 Multiplayer",
                0,
                "maps/mp/gametypes/_clientids.gsc",
                "codmp.elf"),
            new GameVersionProfile(
                "bo2-1.10-zm",
                "1.10 Zombies",
                0,
                "maps/mp/gametypes_zm/_clientids.gsc",
                "codzm.elf")
        };

        private int rpcProcessId;
        private ulong rpcStub;
        private PS4DBG rpcClient;

        public string Id => "bo2";

        public string DisplayName => "Black Ops 2";

        public string AttachButtonText => "Attach BO2";

        public string ProcessName => "codmp.elf";

        public string CompiledScriptDescription => "PS4-compatible compiled Black Ops 2 T6 GSC script";

        public IReadOnlyList<string> ConditionalSymbols => Symbols;

        public IReadOnlyList<GameVersionProfile> Versions => GameVersions;

        public bool CanCompile => false;

        public string CompilerUnavailableMessage =>
            "Black Ops 2 compilation is not included. Select a PS4-compatible compiled T6 script.";

        public CompiledCode Compile(string source)
        {
            throw new NotSupportedException(CompilerUnavailableMessage);
        }

        public bool IsValidCompiledScript(byte[] script)
        {
            return TreyarchCompiledScriptValidator.IsValid(script, CompiledScriptFormat.T6);
        }

        public void InjectCompiledScript(
            PS4DBG ps4,
            libdebug.Process process,
            GameVersionProfile version,
            byte[] script)
        {
            if (ps4 == null)
                throw new ArgumentNullException(nameof(ps4));
            if (process == null)
                throw new ArgumentNullException(nameof(process));
            if (version == null || string.IsNullOrWhiteSpace(version.ScriptHookPath))
                throw new GscInjectionException("Black Ops 2 needs a script hook path before it can inject.");
            if (!IsValidCompiledScript(script))
                throw new GscInjectionException("The selected script is not a valid revision 6 T6 object.");

            string embeddedPath = GetEmbeddedScriptPath(script);
            if (!string.Equals(embeddedPath, version.ScriptHookPath, StringComparison.Ordinal))
            {
                throw new GscInjectionException(
                    "The compiled script targets " + embeddedPath +
                    ", but the selected mode requires " + version.ScriptHookPath + ".");
            }

            ulong findAsset = FindDbFindXAssetHeader(ps4, process.pid);
            if (findAsset == 0)
                throw new GscInjectionException("Could not locate the BO2 asset lookup function for v1.10.");

            ulong pathAddress = 0;
            ulong recordAddress;
            byte[] pathBytes = Encoding.ASCII.GetBytes(version.ScriptHookPath + "\0");
            try
            {
                pathAddress = ps4.AllocateMemory(process.pid, pathBytes.Length);
                if (pathAddress == 0)
                    throw new GscInjectionException("Failed to allocate memory for the BO2 script path.");

                ps4.WriteMemory(process.pid, pathAddress, pathBytes);
                recordAddress = ps4.Call(
                    process.pid,
                    GetRpcStub(ps4, process.pid),
                    findAsset,
                    ScriptParseTreeAssetType,
                    pathAddress,
                    1u,
                    ulong.MaxValue);
            }
            finally
            {
                if (pathAddress != 0)
                {
                    try
                    {
                        ps4.FreeMemory(process.pid, pathAddress, pathBytes.Length);
                    }
                    catch
                    {
                    }
                }
            }

            if (recordAddress == 0)
            {
                throw new GscInjectionException(
                    "BO2 could not find " + version.ScriptHookPath +
                    ". Load the selected mode far enough for the script to be present, then attach again.");
            }

            ScriptParseTreeRecord record = ReadAndValidateRecord(
                ps4,
                process.pid,
                recordAddress,
                version.ScriptHookPath);

            byte[] patchedScript = (byte[])script.Clone();
            ps4.ReadMemory(process.pid, record.BufferAddress + 0x8, sizeof(uint)).CopyTo(patchedScript, 0x8);

            using (var transaction = new RemoteScriptTransaction(ps4, process.pid))
            {
                ulong address = transaction.Allocate(patchedScript);
                transaction.Patch(recordAddress + 0x08, BitConverter.GetBytes(patchedScript.Length));
                transaction.Patch(recordAddress + 0x10, BitConverter.GetBytes(address));
                transaction.Commit();
            }
        }

        internal static IEnumerable<ulong> FindDbFindXAssetHeaderCandidates(byte[] memory, ulong baseAddress)
        {
            if (memory == null || memory.Length < 16)
                yield break;

            for (int index = 0; index <= memory.Length - 5; index++)
            {
                if (memory[index] != 0xBF || memory[index + 1] != 0x31 ||
                    memory[index + 2] != 0 || memory[index + 3] != 0 || memory[index + 4] != 0)
                {
                    continue;
                }

                int callLimit = Math.Min(index + 69, memory.Length - 4);
                for (int call = index + 5; call < callLimit; call++)
                {
                    if (memory[call] != 0xE8)
                        continue;

                    int useLimit = Math.Min(call + 37, memory.Length - 3);
                    for (int use = call + 5; use < useLimit; use++)
                    {
                        if (memory[use] != 0x48 || memory[use + 1] != 0x8B ||
                            memory[use + 2] != 0x40 || memory[use + 3] != 0x10)
                        {
                            continue;
                        }

                        long callAddress = checked((long)baseAddress + call);
                        int relative = BitConverter.ToInt32(memory, call + 1);
                        yield return unchecked((ulong)(callAddress + 5 + relative));
                    }
                }
            }
        }

        internal static ScriptParseTreeRecord ParseRecord(byte[] record)
        {
            if (record == null || record.Length < ScriptRecordSize)
                throw new ArgumentException("A BO2 script record must contain 24 bytes.", nameof(record));

            return new ScriptParseTreeRecord(
                BitConverter.ToUInt64(record, 0),
                BitConverter.ToUInt32(record, 8),
                BitConverter.ToUInt64(record, 0x10));
        }

        internal static string GetEmbeddedScriptPath(byte[] script)
        {
            if (script == null || script.Length < ScriptHeaderSize)
                throw new ArgumentException("A T6 script must contain a complete header.", nameof(script));

            int nameOffset = BitConverter.ToUInt16(script, 0x30);
            if (nameOffset < ScriptHeaderSize || nameOffset >= script.Length)
                throw new ArgumentException("The T6 script name offset is invalid.", nameof(script));

            int end = Array.IndexOf(script, (byte)0, nameOffset);
            if (end < 0)
                throw new ArgumentException("The T6 script name is not null-terminated.", nameof(script));

            return Encoding.ASCII.GetString(script, nameOffset, end - nameOffset);
        }

        private ulong GetRpcStub(PS4DBG ps4, int processId)
        {
            if (rpcStub == 0 || rpcProcessId != processId || !ReferenceEquals(rpcClient, ps4))
            {
                ulong installedStub = ps4.InstallRPC(processId);
                if (installedStub == 0)
                    throw new GscInjectionException("Failed to install the BO2 RPC stub.");

                rpcStub = installedStub;
                rpcProcessId = processId;
                rpcClient = ps4;
            }

            return rpcStub;
        }

        private static ulong FindDbFindXAssetHeader(PS4DBG ps4, int processId)
        {
            ProcessMap processMap = ps4.GetProcessMaps(processId);
            if (processMap?.entries == null)
                return 0;

            var seen = new HashSet<ulong>();
            foreach (MemoryEntry entry in processMap.entries)
            {
                if (!IsReadableExecutable(entry) || entry.end <= entry.start)
                    continue;

                ulong length = entry.end - entry.start;
                for (ulong offset = 0; offset < length; offset += ScanChunkSize)
                {
                    int readSize = (int)Math.Min((ulong)(ScanChunkSize + ScanOverlap), length - offset);
                    byte[] memory;
                    try
                    {
                        memory = ps4.ReadMemory(processId, entry.start + offset, readSize);
                    }
                    catch
                    {
                        break;
                    }

                    foreach (ulong candidate in FindDbFindXAssetHeaderCandidates(memory, entry.start + offset))
                    {
                        if (!seen.Add(candidate) || !HasExpectedFunctionPrologue(ps4, processId, candidate))
                            continue;

                        return candidate;
                    }
                }
            }

            return 0;
        }

        private static bool IsReadableExecutable(MemoryEntry entry)
        {
            const uint required =
                (uint)PS4DBG.VM_PROTECTIONS.VM_PROT_READ |
                (uint)PS4DBG.VM_PROTECTIONS.VM_PROT_EXECUTE;
            return entry != null && (entry.prot & required) == required;
        }

        private static bool HasExpectedFunctionPrologue(PS4DBG ps4, int processId, ulong address)
        {
            try
            {
                byte[] bytes = ps4.ReadMemory(processId, address, 4);
                return bytes.SequenceEqual(new byte[] { 0x55, 0x48, 0x89, 0xE5 });
            }
            catch
            {
                return false;
            }
        }

        private static ScriptParseTreeRecord ReadAndValidateRecord(
            PS4DBG ps4,
            int processId,
            ulong recordAddress,
            string expectedPath)
        {
            ScriptParseTreeRecord record = ReadRecord(ps4, processId, recordAddress);
            if (record.NameAddress == 0 || record.BufferAddress == 0 ||
                record.Size < ScriptHeaderSize || record.Size > 16 * 1024 * 1024)
            {
                throw new GscInjectionException("BO2 returned an invalid script record.");
            }

            string actualPath = ReadNullTerminatedAscii(ps4, processId, record.NameAddress, 256);
            if (!string.Equals(actualPath, expectedPath, StringComparison.Ordinal))
                throw new GscInjectionException("BO2 returned a different script asset than requested.");

            byte[] magic = ps4.ReadMemory(processId, record.BufferAddress, T6Magic.Length);
            if (!magic.SequenceEqual(T6Magic))
                throw new GscInjectionException("The loaded BO2 script is not a revision 6 T6 object.");

            return record;
        }

        private static ScriptParseTreeRecord ReadRecord(PS4DBG ps4, int processId, ulong address)
        {
            return ParseRecord(ps4.ReadMemory(processId, address, ScriptRecordSize));
        }

        private static string ReadNullTerminatedAscii(
            PS4DBG ps4,
            int processId,
            ulong address,
            int maximumLength)
        {
            byte[] bytes = ps4.ReadMemory(processId, address, maximumLength);
            int length = Array.IndexOf(bytes, (byte)0);
            if (length < 0)
                throw new GscInjectionException("The BO2 script path is not null-terminated.");
            return Encoding.ASCII.GetString(bytes, 0, length);
        }

        internal sealed class ScriptParseTreeRecord
        {
            public ScriptParseTreeRecord(
                ulong nameAddress,
                ulong size,
                ulong bufferAddress)
            {
                NameAddress = nameAddress;
                Size = size;
                BufferAddress = bufferAddress;
            }

            public ulong NameAddress { get; }

            public ulong Size { get; }

            public ulong BufferAddress { get; }
        }
    }
}
