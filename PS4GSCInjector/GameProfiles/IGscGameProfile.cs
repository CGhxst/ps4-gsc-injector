using System.Collections.Generic;
using libdebug;
using TreyarchCompiler.Utilities;

namespace PS4GSCInjector.GameProfiles
{
    public interface IGscGameProfile
    {
        string Id { get; }

        string DisplayName { get; }

        string AttachButtonText { get; }

        string ProcessName { get; }

        string CompiledScriptDescription { get; }

        IReadOnlyList<string> ConditionalSymbols { get; }

        IReadOnlyList<GameVersionProfile> Versions { get; }

        bool CanCompile { get; }

        string CompilerUnavailableMessage { get; }

        CompiledCode Compile(string source);

        bool IsValidCompiledScript(byte[] script);

        void InjectCompiledScript(
            PS4DBG ps4,
            libdebug.Process process,
            GameVersionProfile version,
            byte[] script);
    }
}
