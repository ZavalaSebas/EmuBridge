namespace EmuBridge.Models;

public enum InstallOutcome
{
    Success,
    NoKnownCoreForPlatform,
    UnverifiedManifestData,
    DownloadFailed,
    ExtractionFailed,
    ExecutableNotFoundAfterExtraction
}
