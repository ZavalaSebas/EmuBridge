namespace EmuBridge.Models;

// EmuBridge's own curated catalog entry for a known emulator — embedded in the repo
// (Resources/KnownEmulators.json), not fetched live from any third party. Version/DownloadUrl/
// Sha256/ExpectedSizeBytes are all pinned to one specific build, captured by hand by a EmuBridge
// maintainer from the official source. See ARCHITECTURE.md -> ADR-11 for the threat model this
// does and doesn't cover.
public class KnownEmulator
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long ExpectedSizeBytes { get; set; }

    // Path inside the extracted archive to the actual executable — portable 7z/zip archives
    // commonly nest the binary under a subfolder rather than at the archive root.
    public string ExecutableRelativePath { get; set; } = string.Empty;

    public List<KnownEmulatorCore> Cores { get; set; } = [];
}
