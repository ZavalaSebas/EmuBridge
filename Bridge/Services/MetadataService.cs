using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Bridge.Models;
using Microsoft.Extensions.Logging;

namespace Bridge.Services;

public class MetadataService : IMetadataService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Regex TagPattern = new(@"\s*[\(\[][^\)\]]*[\)\]]", RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settingsService;
    private readonly ILibraryRepository _libraryRepository;
    private readonly IImageCacheService _imageCacheService;
    private readonly ILogger<MetadataService> _logger;

    public MetadataService(
        HttpClient httpClient,
        ISettingsService settingsService,
        ILibraryRepository libraryRepository,
        IImageCacheService imageCacheService,
        ILogger<MetadataService> logger)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
        _libraryRepository = libraryRepository;
        _imageCacheService = imageCacheService;
        _logger = logger;
    }

    public async Task<MetadataFetchResult> FetchMissingBoxArtAsync(
        int targetWidth,
        int targetHeight,
        int verticalTargetWidth,
        int verticalTargetHeight,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        var result = new MetadataFetchResult();

        var apiKey = await _settingsService.GetSteamGridDbApiKeyAsync(ct);
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogInformation("No SteamGridDB API key configured; skipping box art fetch.");
            return result;
        }

        var games = await _libraryRepository.GetAllGamesAsync(ct);
        var processed = 0;

        foreach (var game in games)
        {
            ct.ThrowIfCancellationRequested();

            // Only skip once BOTH orientations reached a terminal state — a game whose horizontal
            // grid was already cached before ADR-23 has VerticalStatus defaulting to NotFetched, so
            // it's retroactively reprocessed here rather than left permanently without vertical art.
            var existingBoxArt = await _libraryRepository.GetBoxArtAsync(game.Id, ct);
            if (existingBoxArt is
                {
                    Status: BoxArtStatus.Cached or BoxArtStatus.NotFoundOnProvider,
                    VerticalStatus: BoxArtStatus.Cached or BoxArtStatus.NotFoundOnProvider
                })
            {
                continue;
            }

            var outcome = await FetchBoxArtForGameAsync(
                game, apiKey, targetWidth, targetHeight, verticalTargetWidth, verticalTargetHeight, existingBoxArt, ct);

            switch (outcome)
            {
                case LookupOutcome.Cached:
                    result.Fetched++;
                    break;

                case LookupOutcome.NotFound:
                    result.NotFound++;
                    break;

                case LookupOutcome.RateLimited:
                    result.Failed++;
                    result.StoppedEarlyDueToRateLimit = true;
                    _logger.LogWarning(
                        "SteamGridDB rate limit hit after {Processed}/{Total} games; stopping this batch early. " +
                        "Remaining games stay pending for the next attempt (no Retry-After signal is available — see DEVELOPMENT.md -> Known Limitations).",
                        processed,
                        games.Count);
                    return result;

                case LookupOutcome.AuthFailed:
                    result.Failed++;
                    _logger.LogError(
                        "SteamGridDB rejected the configured API key after {Processed}/{Total} games; stopping this batch early. Fix the key in Settings.",
                        processed,
                        games.Count);
                    return result;

                case LookupOutcome.Failed:
                    result.Failed++;
                    break;
            }

            processed++;
            progress?.Report(processed);
        }

        return result;
    }

    private async Task<LookupOutcome> FetchBoxArtForGameAsync(
        Game game,
        string apiKey,
        int targetWidth,
        int targetHeight,
        int verticalTargetWidth,
        int verticalTargetHeight,
        BoxArt? existingBoxArt,
        CancellationToken ct)
    {
        var searchName = NormalizeGameName(game.Name);

        // Seeded from whatever's already persisted, not blank — a retroactive pass that only needs
        // to resolve one orientation (ARCHITECTURE.md -> ADR-23) must never clobber the other
        // orientation's already-Cached state on any exit path below, including the error ones.
        var status = existingBoxArt?.Status ?? BoxArtStatus.NotFetched;
        var localPath = existingBoxArt?.LocalPath;
        var verticalStatus = existingBoxArt?.VerticalStatus ?? BoxArtStatus.NotFetched;
        var verticalLocalPath = existingBoxArt?.VerticalLocalPath;
        var releaseYear = existingBoxArt?.ReleaseYear;

        var needsHorizontal = status is not (BoxArtStatus.Cached or BoxArtStatus.NotFoundOnProvider);
        var needsVertical = verticalStatus is not (BoxArtStatus.Cached or BoxArtStatus.NotFoundOnProvider);

        SteamGridDbGame? matchedGame;
        try
        {
            matchedGame = await SearchGameAsync(searchName, apiKey, ct);
        }
        catch (SteamGridDbRateLimitException)
        {
            await PersistBoxArtAsync(game.Id, status, localPath, verticalStatus, verticalLocalPath, releaseYear, ct);
            return LookupOutcome.RateLimited;
        }
        catch (SteamGridDbAuthException)
        {
            await PersistBoxArtAsync(game.Id, status, localPath, verticalStatus, verticalLocalPath, releaseYear, ct);
            return LookupOutcome.AuthFailed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Failed to search SteamGridDB for {GameName}.", searchName);
            await PersistBoxArtAsync(game.Id, status, localPath, verticalStatus, verticalLocalPath, releaseYear, ct);
            return LookupOutcome.Failed;
        }

        if (matchedGame is null)
        {
            if (needsHorizontal)
            {
                status = BoxArtStatus.NotFoundOnProvider;
                localPath = null;
            }

            if (needsVertical)
            {
                verticalStatus = BoxArtStatus.NotFoundOnProvider;
                verticalLocalPath = null;
            }

            await PersistBoxArtAsync(game.Id, status, localPath, verticalStatus, verticalLocalPath, releaseYear, ct);
            return LookupOutcome.NotFound;
        }

        releaseYear = matchedGame.ReleaseYear;

        List<SteamGridDbGrid> grids;
        try
        {
            grids = await GetGridsAsync(matchedGame.Id, apiKey, ct);
        }
        catch (SteamGridDbRateLimitException)
        {
            await PersistBoxArtAsync(game.Id, status, localPath, verticalStatus, verticalLocalPath, releaseYear, ct);
            return LookupOutcome.RateLimited;
        }
        catch (SteamGridDbAuthException)
        {
            await PersistBoxArtAsync(game.Id, status, localPath, verticalStatus, verticalLocalPath, releaseYear, ct);
            return LookupOutcome.AuthFailed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Failed to fetch grids for SteamGridDB game {SteamGridDbId}.", matchedGame.Id);
            await PersistBoxArtAsync(game.Id, status, localPath, verticalStatus, verticalLocalPath, releaseYear, ct);
            return LookupOutcome.Failed;
        }

        var anyCached = false;
        var anyFailed = false;

        if (needsHorizontal)
        {
            // Exhaustive, unambiguous split: SteamGridDB filters server-side to only return grids
            // matching one of the 4 dimension strings requested by GetGridsAsync, and none of those
            // 4 pairs (460x215/920x430 horizontal, 600x900/342x482 vertical) is square.
            var horizontalGrid = grids.FirstOrDefault(g => g.Height <= g.Width);
            (status, localPath, var cached, var failed) = await ResolveOrientationAsync(horizontalGrid, targetWidth, targetHeight, ct);
            anyCached |= cached;
            anyFailed |= failed;
        }

        if (needsVertical)
        {
            var verticalGrid = grids.FirstOrDefault(g => g.Height > g.Width);
            (verticalStatus, verticalLocalPath, var cached, var failed) = await ResolveOrientationAsync(verticalGrid, verticalTargetWidth, verticalTargetHeight, ct);
            anyCached |= cached;
            anyFailed |= failed;
        }

        await PersistBoxArtAsync(game.Id, status, localPath, verticalStatus, verticalLocalPath, releaseYear, ct);

        if (anyCached)
        {
            return LookupOutcome.Cached;
        }

        return anyFailed ? LookupOutcome.Failed : LookupOutcome.NotFound;
    }

    private async Task<(BoxArtStatus Status, string? LocalPath, bool Cached, bool Failed)> ResolveOrientationAsync(
        SteamGridDbGrid? grid, int width, int height, CancellationToken ct)
    {
        if (grid is null)
        {
            return (BoxArtStatus.NotFoundOnProvider, null, false, false);
        }

        var localPath = await _imageCacheService.GetOrCacheImageAsync(grid.Url, width, height, ct);
        return localPath is null
            ? (BoxArtStatus.FetchFailed, null, false, true)
            : (BoxArtStatus.Cached, localPath, true, false);
    }

    private async Task<SteamGridDbGame?> SearchGameAsync(string query, string apiKey, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{Config.SteamGridDbBaseUrl}/search/autocomplete/{Uri.EscapeDataString(query)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await _httpClient.SendAsync(request, ct);
        ThrowIfSpecialStatus(response);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<SteamGridDbResponse<List<SteamGridDbGame>>>(JsonOptions, ct);
        return body?.Data?.FirstOrDefault();
    }

    // Requests both horizontal (460x215/920x430) and vertical (600x900/342x482) dimensions in one
    // call — a single dimensions-filtered request, not two separate calls — confirmed real values
    // via SteamGridDB's own API docs (see ARCHITECTURE.md -> ADR-23); the wrapper source itself
    // doesn't hardcode them, same caveat already noted for the release_date field (ADR-19).
    private async Task<List<SteamGridDbGrid>> GetGridsAsync(int steamGridDbGameId, string apiKey, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{Config.SteamGridDbBaseUrl}/grids/game/{steamGridDbGameId}?dimensions=460x215,920x430,600x900,342x482");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await _httpClient.SendAsync(request, ct);
        ThrowIfSpecialStatus(response);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<SteamGridDbResponse<List<SteamGridDbGrid>>>(JsonOptions, ct);
        return body?.Data ?? [];
    }

    private static void ThrowIfSpecialStatus(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new SteamGridDbRateLimitException();
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new SteamGridDbAuthException();
        }
    }

    private async Task PersistBoxArtAsync(
        Guid gameId,
        BoxArtStatus status,
        string? localPath,
        BoxArtStatus verticalStatus,
        string? verticalLocalPath,
        int? releaseYear,
        CancellationToken ct)
    {
        await _libraryRepository.UpsertBoxArtAsync(
            new BoxArt
            {
                GameId = gameId,
                Status = status,
                LocalPath = localPath,
                VerticalStatus = verticalStatus,
                VerticalLocalPath = verticalLocalPath,
                LastAttemptUtc = DateTime.UtcNow,
                ReleaseYear = releaseYear
            },
            ct);
    }

    // No FR currently needs fuzzy/scored matching (see design discussion) — strips common
    // No-Intro/Redump-style parenthetical/bracketed tags (region, revision, etc.) since those
    // reliably hurt SteamGridDB search match quality and are cheap to remove.
    private static string NormalizeGameName(string rawName)
    {
        var stripped = TagPattern.Replace(rawName, string.Empty).Trim();
        return string.IsNullOrWhiteSpace(stripped) ? rawName : stripped;
    }

    private enum LookupOutcome
    {
        Cached,
        NotFound,
        RateLimited,
        AuthFailed,
        Failed
    }

    private class SteamGridDbRateLimitException : Exception;

    private class SteamGridDbAuthException : Exception;

    private class SteamGridDbResponse<T>
    {
        public bool Success { get; set; }
        public T? Data { get; set; }
        public List<string>? Errors { get; set; }
    }

    private class SteamGridDbGame
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        // Unix seconds — confirmed against the official node-steamgriddb wrapper's TypeScript
        // definition (`release_date: number`), not assumed. 0/absent means SteamGridDB has no
        // release date for this game, same convention as an unset Unix timestamp elsewhere.
        // Real API field is snake_case; PropertyNameCaseInsensitive only handles casing, not
        // underscore-vs-PascalCase, so this needs an explicit name mapping.
        [JsonPropertyName("release_date")]
        public long? ReleaseDate { get; set; }

        public int? ReleaseYear => ReleaseDate is > 0
            ? DateTimeOffset.FromUnixTimeSeconds(ReleaseDate.Value).UtcDateTime.Year
            : null;
    }

    private class SteamGridDbGrid
    {
        public int Id { get; set; }
        public string Url { get; set; } = string.Empty;

        // Confirmed real fields on the wrapper's SGDBImage interface (both lowercase `number` in
        // TypeScript). Used to classify horizontal vs vertical without hardcoding the 4 requested
        // dimension strings a second time — see ARCHITECTURE.md -> ADR-23.
        public int Width { get; set; }
        public int Height { get; set; }
    }
}
