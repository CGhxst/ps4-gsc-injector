using System.Collections.Generic;

namespace PS4GSCInjector.GameProfiles
{
    public static class GameProfileRegistry
    {
        private static readonly IReadOnlyList<IGscGameProfile> Profiles = new IGscGameProfile[]
        {
            new Bo2GameProfile(),
            new Bo3GameProfile(),
            new Bo4GameProfile()
        };

        public static IReadOnlyList<IGscGameProfile> All => Profiles;
    }
}
