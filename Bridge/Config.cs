using System.IO;

namespace Bridge;

public static class Config
{
    public const string AppName = "Bridge";

    public const string UnknownPlatformId = "unknown";
    public const string UnknownPlatformName = "Unknown System";

    public const string SeedSystemsResourceName = "Bridge.Resources.SeedSystems.json";

    public const string SteamGridDbBaseUrl = "https://www.steamgriddb.com/api/v2";

    // Phase 1 cover grid tile size — a placeholder default (2:3, matching typical box art
    // proportions), not a final UI decision. Tune when the actual grid cell size is designed.
    public const int CoverWidth = 200;
    public const int CoverHeight = 300;

    public static string AppDataPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppName);

    public static string LibraryDbPath => Path.Combine(AppDataPath, "bridge.db");
    public static string SettingsPath => Path.Combine(AppDataPath, "settings.json");
    public static string ImageCachePath => Path.Combine(AppDataPath, "ImageCache");
}
