namespace Bridge.Models;

public class MetadataFetchResult
{
    public int Fetched { get; set; }
    public int NotFound { get; set; }
    public int Failed { get; set; }

    /// <summary>
    /// True if the batch stopped processing early because SteamGridDB returned HTTP 429.
    /// SteamGridDB does not document or send a Retry-After (or equivalent) header — confirmed
    /// against the official Node.js wrapper's source and the community .NET wrapper, neither of
    /// which read or expose one. See DEVELOPMENT.md -> Known Limitations: there is no reliable
    /// signal for "safe to retry at". Remaining games stay FetchFailed and are retried on the
    /// next batch run, whenever that happens to be.
    /// </summary>
    public bool StoppedEarlyDueToRateLimit { get; set; }
}
