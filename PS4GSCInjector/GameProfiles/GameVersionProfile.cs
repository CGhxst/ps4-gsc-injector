namespace PS4GSCInjector.GameProfiles
{
    public sealed class GameVersionProfile
    {
        public GameVersionProfile(
            string id,
            string displayName,
            ulong targetScriptAddress,
            string scriptHookPath = null,
            string processName = null)
        {
            Id = id;
            DisplayName = displayName;
            TargetScriptAddress = targetScriptAddress;
            ScriptHookPath = scriptHookPath;
            ProcessName = processName;
        }

        public string Id { get; }

        public string DisplayName { get; }

        public ulong TargetScriptAddress { get; }

        public string ScriptHookPath { get; }

        public string ProcessName { get; }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
