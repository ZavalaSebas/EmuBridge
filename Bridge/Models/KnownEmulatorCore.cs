namespace Bridge.Models;

// A single libretro core within a KnownEmulator's catalog entry, mapped to one Bridge Platform.
// Unlike KnownEmulator.Version (a stable, numbered release), cores have no separate "stable"
// distribution channel — buildbot.libretro.com/nightly/.../latest/ is the real channel, confirmed
// against an official RetroArch repo issue. CapturedAt records when Sha256/ExpectedSizeBytes were
// pinned from that rolling source; a later re-download of the same DownloadUrl is expected to
// produce a different file, not a discrepancy to investigate. See ARCHITECTURE.md -> ADR-11.
public class KnownEmulatorCore
{
    public string Id { get; set; } = string.Empty;
    public string PlatformId { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long ExpectedSizeBytes { get; set; }
    public string CoreFileName { get; set; } = string.Empty;
    public string CapturedAt { get; set; } = string.Empty;
}
