using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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

            var existingBoxArt = await _libraryRepository.GetBoxArtAsync(game.Id, ct);
            if (existingBoxArt is { Status: BoxArtStatus.Cached or BoxArtStatus.NotFoundOnProvider })
            {
                continue;
            }

            var outcome = await FetchBoxArtForGameAsync(game, apiKey, targetWidth, targetHeight, ct);

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
        CancellationToken ct)
    {
        var searchName = NormalizeGameName(game.Name);

        SteamGridDbGame? matchedGame;
        try
        {
            matchedGame = await SearchGameAsync(searchName, apiKey, ct);
        }
        catch (SteamGridDbRateLimitException)
        {
            await PersistBoxArtAsync(game.Id, BoxArtStatus.FetchFailed, null, ct);
            return LookupOutcome.RateLimited;
        }
        catch (SteamGridDbAuthException)
        {
            await PersistBoxArtAsync(game.Id, BoxArtStatus.FetchFailed, null, ct);
            return LookupOutcome.AuthFailed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Failed to search SteamGridDB for {GameName}.", searchName);
            await PersistBoxArtAsync(game.Id, BoxArtStatus.FetchFailed, null, ct);
            return LookupOutcome.Failed;
        }

        if (matchedGame is null)
        {
            await PersistBoxArtAsync(game.Id, BoxArtStatus.NotFoundOnProvider, null, ct);
            return LookupOutcome.NotFound;
        }

        SteamGridDbGrid? grid;
        try
        {
            grid = await GetFirstGridAsync(matchedGame.Id, apiKey, ct);
        }
        catch (SteamGridDbRateLimitException)
        {
            await PersistBoxArtAsync(game.Id, BoxArtStatus.FetchFailed, null, ct);
            return LookupOutcome.RateLimited;
        }
        catch (SteamGridDbAuthException)
        {
            await PersistBoxArtAsync(game.Id, BoxArtStatus.FetchFailed, null, ct);
            return LookupOutcome.AuthFailed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Failed to fetch grids for SteamGridDB game {SteamGridDbId}.", matchedGame.Id);
            await PersistBoxArtAsync(game.Id, BoxArtStatus.FetchFailed, null, ct);
            return LookupOutcome.Failed;
        }

        if (grid is null)
        {
            await PersistBoxArtAsync(game.Id, BoxArtStatus.NotFoundOnProvider, null, ct);
            return LookupOutcome.NotFound;
        }

        var localPath = await _imageCacheService.GetOrCacheImageAsync(grid.Url, targetWidth, targetHeight, ct);
        if (localPath is null)
        {
            await PersistBoxArtAsync(game.Id, BoxArtStatus.FetchFailed, null, ct);
            return LookupOutcome.Failed;
        }

        await PersistBoxArtAsync(game.Id, BoxArtStatus.Cached, localPath, ct);
        return LookupOutcome.Cached;
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

    private async Task<SteamGridDbGrid?> GetFirstGridAsync(int steamGridDbGameId, string apiKey, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{Config.SteamGridDbBaseUrl}/grids/game/{steamGridDbGameId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await _httpClient.SendAsync(request, ct);
        ThrowIfSpecialStatus(response);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<SteamGridDbResponse<List<SteamGridDbGrid>>>(JsonOptions, ct);
        return body?.Data?.FirstOrDefault();
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

    private async Task PersistBoxArtAsync(Guid gameId, BoxArtStatus status, string? localPath, CancellationToken ct)
    {
        await _libraryRepository.UpsertBoxArtAsync(
            new BoxArt
            {
                GameId = gameId,
                Status = status,
                LocalPath = localPath,
                LastAttemptUtc = DateTime.UtcNow
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
    }

    private class SteamGridDbGrid
    {
        public int Id { get; set; }
        public string Url { get; set; } = string.Empty;
    }
}
