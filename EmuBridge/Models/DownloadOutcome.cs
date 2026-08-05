namespace EmuBridge.Models;

public enum DownloadOutcome
{
    Success,
    HashMismatch,
    SizeExceeded,
    NetworkError,
    UntrustedSource
}
