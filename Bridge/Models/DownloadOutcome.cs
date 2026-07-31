namespace Bridge.Models;

public enum DownloadOutcome
{
    Success,
    HashMismatch,
    SizeExceeded,
    NetworkError
}
