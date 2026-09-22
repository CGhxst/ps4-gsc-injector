using System;
using System.Collections.Generic;
using libdebug;

namespace PS4GSCInjector.GameProfiles
{
    public abstract class ScriptPointerGameProfile : IGscGameProfile
    {
        protected ScriptPointerGameProfile(ScriptPointerOffsets offsets)
        {
            Offsets = offsets;
        }

        protected ScriptPointerOffsets Offsets { get; }

        public abstract string Id { get; }

        public abstract string DisplayName { get; }

        public abstract string AttachButtonText { get; }

        public abstract string ProcessName { get; }

        public abstract string CompiledScriptDescription { get; }

        public abstract bool CanCompile { get; }

        public abstract string CompilerUnavailableMessage { get; }

        public abstract IReadOnlyList<string> ConditionalSymbols { get; }

        public abstract IReadOnlyList<GameVersionProfile> Versions { get; }

        public abstract TreyarchCompiler.Utilities.CompiledCode Compile(string source);

        public abstract bool IsValidCompiledScript(byte[] script);

        public virtual void InjectCompiledScript(
            PS4DBG ps4,
            libdebug.Process process,
            GameVersionProfile version,
            byte[] script)
        {
            if (ps4 == null) throw new ArgumentNullException(nameof(ps4));
            if (process == null) throw new ArgumentNullException(nameof(process));
            if (version == null) throw new ArgumentNullException(nameof(version));
            if (!IsValidCompiledScript(script))
                throw new GscInjectionException("The selected script is invalid for this game.");

            ulong targetScriptAddress = ResolveTargetScriptAddress(ps4, process, version, script);
            if (targetScriptAddress == 0)
                throw new GscInjectionException("This target could not resolve a script hook in memory.");

            ulong pointerAddress = checked(targetScriptAddress + (ulong)Offsets.ScriptPointerOffset);
            ulong originalAddress = ps4.ReadMemory<ulong>(process.pid, pointerAddress);
            if (originalAddress == 0 ||
                !TreyarchCompiledScriptValidator.IsValid(
                    ps4.ReadMemory(process.pid, originalAddress, 0x50), CompiledScriptFormat.T7, true))
                throw new GscInjectionException("The BO3 hook does not contain a T7 script. Check the game version and loaded mode.");

            byte[] patchedScript = (byte[])script.Clone();
            ps4.ReadMemory(process.pid, originalAddress + (ulong)Offsets.ChecksumReadOffset, sizeof(uint))
                .CopyTo(patchedScript, Offsets.ChecksumWriteOffset);
            using (var transaction = new RemoteScriptTransaction(ps4, process.pid))
            {
                ulong address = transaction.Allocate(patchedScript);
                transaction.Patch(pointerAddress, BitConverter.GetBytes(address));
                transaction.Commit();
            }
        }

        protected virtual ulong ResolveTargetScriptAddress(
            PS4DBG ps4,
            libdebug.Process process,
            GameVersionProfile version,
            byte[] script)
        {
            return version.TargetScriptAddress;
        }
    }
}
