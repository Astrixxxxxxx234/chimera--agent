// LocalConfig — overrides for local testing only
// In production Staging.Dir is C:\Users\Public, which is the excluded/staging zone.
// For local testing the loot zip is also copied to LootDir so you can inspect it.

namespace Chimera
{
    public static class LocalConfig
    {
        // Where to copy loot zip + screenshots for local inspection
        public const string LootDir = @"C:\projects\chimera-local-test";
    }
}
