using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using EmuBridge.Models;
using Microsoft.Extensions.Logging;

namespace EmuBridge.Services;

public class TheGamesDbService : ITheGamesDbService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Regex TagPattern = new(@"\s*[\(\[][^\)\]]*[\)\]]", RegexOptions.Compiled);

    // Screenshot thumbnail size for the detail panel — a placeholder default, not a final UI
    // decision, same status as Config.CoverWidth/CoverHeight.
    private const int ScreenshotWidth = 200;
    private const int ScreenshotHeight = 150;

    // Confirmed real via TheGamesDB's own /Platforms endpoint during the metadata-source decision
    // research, 2026-08-04 (see PLAN.md -> Timeline) — not guessed from naming convention. A few of
    // EmuBridge's 15 seed platforms (genesis, wonderswan) map to TWO real TheGamesDB catalog entries
    // for what's the same hardware under different regional names (Genesis/Mega Drive,
    // WonderSwan/WonderSwan Color) — both are tried, primary first, since a given game can be
    // catalogued under either depending on region.
    private static readonly Dictionary<string, int[]> PlatformIds = new()
    {
        ["nes"] = [7],
        ["snes"] = [6],
        ["n64"] = [3],
        ["gb"] = [4],
        ["gbc"] = [41],
        ["gba"] = [5],
        ["nds"] = [8],
        ["genesis"] = [18, 36],
        ["sms"] = [35],
        ["gamegear"] = [20],
        ["atari2600"] = [22],
        ["atari7800"] = [27],
        ["pcengine"] = [34],
        ["lynx"] = [4924],
        ["wonderswan"] = [4925, 4926]
    };

    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settingsService;
    private readonly ILibraryRepository _libraryRepository;
    private readonly IImageCacheService _imageCacheService;
    private readonly ILogger<TheGamesDbService> _logger;

    public TheGamesDbService(
        HttpClient httpClient,
        ISettingsService settingsService,
        ILibraryRepository libraryRepository,
        IImageCacheService imageCacheService,
        ILogger<TheGamesDbService> logger)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
        _libraryRepository = libraryRepository;
        _imageCacheService = imageCacheService;
        _logger = logger;
    }

    public async Task<TheGamesDbOutcome> FetchDescriptionAndScreenshotsAsync(Game game, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var apiKey = await _settingsService.GetTheGamesDbApiKeyAsync(ct);
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogInformation("No TheGamesDB API key configured; skipping description/screenshot fetch.");
            return TheGamesDbOutcome.NoKeyConfigured;
        }

        var existing = await _libraryRepository.GetBoxArtAsync(game.Id, ct);

        // Cached/NotFoundOnProvider are terminal - matches MetadataService's exact skip condition
        // for box art. FetchFailed (network blip, corrupted response, or a rate limit) is
        // deliberately NOT terminal - it's retried on the next window-open, same precedent as
        // MetadataService's own FetchFailed handling for box art.
        if (existing?.DescriptionStatus is BoxArtStatus.Cached or BoxArtStatus.NotFoundOnProvider)
        {
            return existing.DescriptionStatus == BoxArtStatus.Cached ? TheGamesDbOutcome.Cached : TheGamesDbOutcome.NotFound;
        }

        TheGamesDbGame? matched;
        try
        {
            matched = await SearchGameAsync(NormalizeGameName(game.Name), game.PlatformId, apiKey, ct);
        }
        catch (TheGamesDbRateLimitException ex)
        {
            await PersistAsync(game, existing, BoxArtStatus.FetchFailed, null, [], DateTime.UtcNow.AddSeconds(ex.RefreshTimerSeconds), ct);
            return TheGamesDbOutcome.RateLimited;
        }
        catch (TheGamesDbAuthException)
        {
            _logger.LogError("TheGamesDB rejected the configured API key. Fix the key in Settings.");
            await PersistAsync(game, existing, BoxArtStatus.FetchFailed, null, [], null, ct);
            return TheGamesDbOutcome.Failed;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _logger.LogWarning(ex, "Failed to search TheGamesDB for {GameName}.", game.Name);
            await PersistAsync(game, existing, BoxArtStatus.FetchFailed, null, [], null, ct);
            return TheGamesDbOutcome.Failed;
        }

        if (matched is null)
        {
            await PersistAsync(game, existing, BoxArtStatus.NotFoundOnProvider, null, [], null, ct);
            return TheGamesDbOutcome.NotFound;
        }

        // A screenshot-fetch failure degrades to an empty screenshot list, not a description
        // blanked out - the description already resolved successfully and shouldn't be discarded
        // over an unrelated second call failing.
        var screenshotPaths = new List<string>();
        try
        {
            var screenshotUrls = await GetScreenshotUrlsAsync(matched.Id, apiKey, ct);
            foreach (var url in screenshotUrls)
            {
                var localPath = await _imageCacheService.GetOrCacheImageAsync(url, ScreenshotWidth, ScreenshotHeight, ct);
                if (localPath is not null)
                {
                    screenshotPaths.Add(localPath);
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TheGamesDbRateLimitException or TheGamesDbAuthException)
        {
            _logger.LogWarning(ex, "Failed to fetch TheGamesDB screenshots for {GameName}.", game.Name);
        }

        await PersistAsync(game, existing, BoxArtStatus.Cached, matched.Overview, screenshotPaths, null, ct);
        return TheGamesDbOutcome.Cached;
    }

    private async Task<TheGamesDbGame?> SearchGameAsync(string gameName, string emuBridgePlatformId, string apiKey, CancellationToken ct)
    {
        if (!PlatformIds.TryGetValue(emuBridgePlatformId, out var candidateIds))
        {
            // Every one of EmuBridge's 15 seed platforms is mapped above - an unmapped platform means
            // a new one was added to SeedSystems.json without updating this table. Confirmed
            // empirically (Ninja Gaiden case, metadata-source decision research) that an unfiltered
            // name-only search can silently match the wrong platform's entry - not worth risking a
            // false-positive match for a case that shouldn't exist yet.
            _logger.LogWarning(
                "No TheGamesDB platform mapping for EmuBridge platform '{PlatformId}'; skipping search rather than risk an unfiltered mismatch.",
                emuBridgePlatformId);
            return null;
        }

        foreach (var platformId in candidateIds)
        {
            var match = await SearchByNameAsync(gameName, platformId, apiKey, ct);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private async Task<TheGamesDbGame?> SearchByNameAsync(string gameName, int platformId, string apiKey, CancellationToken ct)
    {
        var url = $"{Config.TheGamesDbBaseUrl}/Games/ByGameName?apikey={apiKey}&name={Uri.EscapeDataString(gameName)}&fields=overview&filter[platform]={platformId}";

        using var response = await _httpClient.GetAsync(url, ct);
        await ThrowIfSpecialStatusAsync(response, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<TheGamesDbGameSearchResponse>(JsonOptions, ct);
        return body?.Data?.Games?.FirstOrDefault();
    }

    // Real response shape confirmed via a live authenticated call, 2026-08-04 (see PLAN.md ->
    // Timeline): data.images is keyed by the TheGamesDB game id (as a string), each value a flat
    // list of images across every type (screenshot/fanart/boxart/clearlogo/titlescreen) - filtered
    // here to "screenshot" only, matching ADR-19's box-art-is-not-a-screenshot distinction.
    private async Task<List<string>> GetScreenshotUrlsAsync(int theGamesDbGameId, string apiKey, CancellationToken ct)
    {
        var url = $"{Config.TheGamesDbBaseUrl}/Games/Images?apikey={apiKey}&games_id={theGamesDbGameId}";

        using var response = await _httpClient.GetAsync(url, ct);
        await ThrowIfSpecialStatusAsync(response, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<TheGamesDbImagesResponse>(JsonOptions, ct);
        var baseUrl = body?.Data?.BaseUrl?.Original;
        if (string.IsNullOrEmpty(baseUrl))
        {
            return [];
        }

        if (body?.Data?.Images is null || !body.Data.Images.TryGetValue(theGamesDbGameId.ToString(), out var images))
        {
            return [];
        }

        return images
            .Where(img => img.Type == "screenshot")
            .Select(img => baseUrl + img.Filename)
            .ToList();
    }

    // TheGamesDB returns HTTP 403 for both "rate limit exceeded" and "invalid API key" - confirmed
    // real via a live call during the metadata-source decision research, 2026-08-04 (an invalid key
    // produced {"code":403,"status":"Invalid API key was provided.","allowance_refresh_timer":0}).
    // allowance_refresh_timer > 0 is the only field that reliably tells them apart in that response
    // - an invalid key has no active allowance window at all (observed as 0), while a real rate
    // limit should carry a real countdown. The true rate-limit response body itself was never
    // directly observed (the key never actually hit its cap during this research) - this is an
    // evidence-grounded heuristic, not a fully proven one; revisit if it's ever seen to misclassify.
    private static async Task ThrowIfSpecialStatusAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden))
        {
            return;
        }

        try
        {
            var body = await response.Content.ReadFromJsonAsync<TheGamesDbErrorEnvelope>(JsonOptions, ct);
            if (body?.AllowanceRefreshTimer > 0)
            {
                throw new TheGamesDbRateLimitException(body.AllowanceRefreshTimer);
            }
        }
        catch (JsonException)
        {
            // Malformed error body - still an auth-class failure, fall through below.
        }

        throw new TheGamesDbAuthException();
    }

    private async Task PersistAsync(
        Game game,
        BoxArt? existing,
        BoxArtStatus descriptionStatus,
        string? description,
        List<string> screenshotPaths,
        DateTime? rateLimitResetUtc,
        CancellationToken ct)
    {
        var boxArt = existing ?? new BoxArt { GameId = game.Id };
        boxArt.DescriptionStatus = descriptionStatus;
        boxArt.Description = description;
        boxArt.ScreenshotLocalPaths = screenshotPaths;
        boxArt.DescriptionRateLimitResetUtc = rateLimitResetUtc;

        await _libraryRepository.UpsertBoxArtAsync(boxArt, ct);
    }

    // Same normalization MetadataService applies before searching SteamGridDB - No-Intro/Redump
    // style parenthetical/bracketed tags (region, revision, etc.) hurt title-search match quality
    // on TheGamesDB too, confirmed by the same class of real, non-hypothetical filenames.
    private static string NormalizeGameName(string rawName)
    {
        var stripped = TagPattern.Replace(rawName, string.Empty).Trim();
        return string.IsNullOrWhiteSpace(stripped) ? rawName : stripped;
    }

    private class TheGamesDbRateLimitException(int refreshTimerSeconds) : Exception
    {
        public int RefreshTimerSeconds { get; } = refreshTimerSeconds;
    }

    private class TheGamesDbAuthException : Exception;

    private class TheGamesDbGameSearchResponse
    {
        public TheGamesDbSearchData? Data { get; set; }
    }

    private class TheGamesDbSearchData
    {
        public int Count { get; set; }
        public List<TheGamesDbGame>? Games { get; set; }
    }

    private class TheGamesDbGame
    {
        public int Id { get; set; }

        [JsonPropertyName("game_title")]
        public string GameTitle { get; set; } = string.Empty;

        public string? Overview { get; set; }
    }

    private class TheGamesDbImagesResponse
    {
        public TheGamesDbImagesData? Data { get; set; }
    }

    private class TheGamesDbImagesData
    {
        [JsonPropertyName("base_url")]
        public TheGamesDbBaseUrls? BaseUrl { get; set; }

        public Dictionary<string, List<TheGamesDbImage>>? Images { get; set; }
    }

    private class TheGamesDbBaseUrls
    {
        public string? Original { get; set; }
    }

    private class TheGamesDbImage
    {
        public string Type { get; set; } = string.Empty;
        public string Filename { get; set; } = string.Empty;
    }

    private class TheGamesDbErrorEnvelope
    {
        public int Code { get; set; }
        public string? Status { get; set; }

        [JsonPropertyName("allowance_refresh_timer")]
        public int AllowanceRefreshTimer { get; set; }
    }
}
