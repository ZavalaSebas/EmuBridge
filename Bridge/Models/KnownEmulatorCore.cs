namespace Bridge.Models;

// A single libretro core within a KnownEmulator's catalog entry, mapped to one Bridge Platform.
public class KnownEmulatorCore
{
    public string Id { get; set; } = string.Empty;
    public string PlatformId { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long ExpectedSizeBytes { get; set; }
    public string CoreFileName { get; set; } = string.Empty;
}
