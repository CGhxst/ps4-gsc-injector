using System;
using System.Collections.Generic;
using libdebug;
using TreyarchCompiler;
using TreyarchCompiler.Utilities;

namespace PS4GSCInjector.GameProfiles
{
    public sealed class Bo4GameProfile : IGscGameProfile
    {
        private static readonly IReadOnlyList<string> Symbols = new[] { "BO4", "T8", "PS4", "__PS4", "_GSC" };
        private const ulong TargetCompilerScriptHash = 0x124CECFF7280BE52;
        private const int ScriptMemoryAlignment = 16;

        private static readonly IReadOnlyList<GameVersionProfile> GameVersions = new[]
        {
            new GameVersionProfile("bo4-1.26-zm", "1.26 Zombies", 0, @"scripts\zm_common\load.gsc"),
            new GameVersionProfile("bo4-1.26-mp", "1.26 Multiplayer", 0, @"scripts\mp_common\bb.gsc"),
            new GameVersionProfile("bo4-1.26-common", "1.26 Frontend/Common", 0, @"scripts\core_common\load_shared.gsc")
        };

        public string Id => "bo4";

        public string DisplayName => "Black Ops 4";

        public string AttachButtonText => "Attach BO4";

        public string ProcessName => "eboot.bin";

        public string CompiledScriptDescription => "compiled Black Ops 4 T8 GSC script";

        public bool CanCompile => true;

        public string CompilerUnavailableMessage => string.Empty;

        public IReadOnlyList<string> ConditionalSymbols => Symbols;

        public IReadOnlyList<GameVersionProfile> Versions => GameVersions;

        public CompiledCode Compile(string source)
        {
            return Compiler.CompileT8(source);
        }

        public bool IsValidCompiledScript(byte[] script)
        {
            return TreyarchCompiledScriptValidator.IsValid(script, CompiledScriptFormat.T8);
        }

        public void InjectCompiledScript(
            libdebug.PS4DBG ps4,
            Process process,
            GameVersionProfile version,
            byte[] script)
        {
            if (ps4 == null) throw new ArgumentNullException(nameof(ps4));
            if (process == null) throw new ArgumentNullException(nameof(process));
            if (version == null) throw new ArgumentNullException(nameof(version));
            if (!IsValidCompiledScript(script))
                throw new GscInjectionException("The selected script is not a valid T8 script.");

            if (string.IsNullOrWhiteSpace(version.ScriptHookPath))
                throw new GscInjectionException("Black Ops 4 needs a script hook before it can inject.");

            var scriptNameHash = T8ScriptHash.Hash64(version.ScriptHookPath);
            if (!MemoryScriptPointerLocator.TryFindT8ScriptParseTreeEntries(
                ps4,
                process,
                scriptNameHash,
                TargetCompilerScriptHash,
                out var surrogateEntry,
                out var targetEntry))
            {
                throw new GscInjectionException(
                    "Could not auto-locate the BO4 script hook " + version.ScriptHookPath +
                    ". Make sure the matching game mode is loaded far enough for that script to exist in memory.");
            }

            byte[] patchedScript = (byte[])script.Clone();
            ps4.ReadMemory(process.pid, targetEntry.BufferAddress + 0x8, sizeof(uint)).CopyTo(patchedScript, 0x8);
            BitConverter.GetBytes(TargetCompilerScriptHash).CopyTo(patchedScript, 0x10);

            using (var transaction = new RemoteScriptTransaction(ps4, process.pid))
            {
                ulong address = transaction.Allocate(patchedScript, ScriptMemoryAlignment);
                EnsureSurrogateIncludesTarget(ps4, process, surrogateEntry, transaction);
                transaction.Patch(targetEntry.EntryAddress + 0x18, BitConverter.GetBytes(patchedScript.Length));
                transaction.Patch(targetEntry.EntryAddress + 0x10, BitConverter.GetBytes(address));
                transaction.Commit();
            }
        }

        internal static ulong AlignUp(ulong address, int alignment)
        {
            if (alignment <= 0 || (alignment & (alignment - 1)) != 0)
                throw new ArgumentOutOfRangeException(nameof(alignment), "Alignment must be a positive power of two.");

            ulong mask = (ulong)(alignment - 1);
            return checked((address + mask) & ~mask);
        }

        private static void EnsureSurrogateIncludesTarget(
            PS4DBG ps4, Process process,
            MemoryScriptPointerLocator.T8ScriptParseTreeEntry entry,
            RemoteScriptTransaction transaction)
        {
            byte[] script = ps4.ReadMemory(process.pid, entry.BufferAddress, entry.Size);
            int appendOffset = GetIncludeAppendOffset(script, TargetCompilerScriptHash);
            if (appendOffset < 0) return;
            transaction.Patch(entry.BufferAddress + (ulong)appendOffset, BitConverter.GetBytes(TargetCompilerScriptHash));
            ushort count = BitConverter.ToUInt16(script, 0x58);
            transaction.Patch(entry.BufferAddress + 0x58, BitConverter.GetBytes(checked((ushort)(count + 1))));
        }

        internal static int GetIncludeAppendOffset(byte[] script, ulong targetHash)
        {
            if (!TreyarchCompiledScriptValidator.IsValid(script, CompiledScriptFormat.T8, true))
                throw new GscInjectionException("The BO4 hook has an invalid script layout.");
            uint offset = BitConverter.ToUInt32(script, 0x18);
            ushort count = BitConverter.ToUInt16(script, 0x58);
            if (offset < 0x60 || (ulong)offset + (ulong)count * 8 > (ulong)script.Length)
                throw new GscInjectionException("The BO4 hook include table is outside the script.");
            for (int i = 0; i < count; i++)
                if (BitConverter.ToUInt64(script, checked((int)offset + i * 8)) == targetHash)
                    return -1;
            if (count == ushort.MaxValue)
                throw new GscInjectionException("The BO4 hook include table is full.");

            ulong end = (ulong)offset + (ulong)count * 8;
            ulong limit = (ulong)script.Length;
            // All section starts that can bound the include table in VM 0x36.
            foreach (int field in new[] { 0x20, 0x24, 0x2C, 0x30, 0x38, 0x40, 0x44, 0x4C })
            {
                uint next = BitConverter.ToUInt32(script, field);
                if (next >= end && next >= 0x60 && next < limit) limit = next;
            }
            if (offset < 0x60 || end + 8 > limit)
                throw new GscInjectionException("The BO4 hook has no room for another include. Injection was cancelled.");
            for (int i = 0; i < 8; i++)
                if (script[(int)end + i] != 0)
                    throw new GscInjectionException("The BO4 include padding is occupied. Injection was cancelled.");
            return (int)end;
        }
    }
}
