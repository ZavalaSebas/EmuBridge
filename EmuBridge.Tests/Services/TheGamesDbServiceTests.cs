using System.Net;
using System.Net.Http;
using EmuBridge.Models;
using EmuBridge.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmuBridge.Tests.Services;

public class TheGamesDbServiceTests
{
    // Real shapes confirmed via live authenticated calls during the metadata-source decision
    // research, 2026-08-04 (see PLAN.md -> Timeline) - not guessed from the docs alone.
    private const string SearchFoundJson = """{"code":200,"status":"Success","data":{"count":1,"games":[{"id":9054,"game_title":"Bahamut Lagoon","platform":6,"overview":"A tale of dragons and war."}]}}""";
    private const string SearchNotFoundJson = """{"code":200,"status":"Success","data":{"count":0,"games":[]}}""";
    private const string ImagesFoundJson = """{"code":200,"status":"Success","data":{"count":1,"base_url":{"original":"https://cdn.thegamesdb.net/images/original/"},"images":{"9054":[{"id":1,"type":"screenshot","filename":"screenshots/9054-1.jpg"},{"id":2,"type":"screenshot","filename":"screenshots/9054-2.jpg"},{"id":3,"type":"fanart","filename":"fanart/9054-1.jpg"}]}}}""";
    private const string ImagesEmptyJson = """{"code":200,"status":"Success","data":{"count":0,"base_url":{"original":"https://cdn.thegamesdb.net/images/original/"},"images":{}}}""";

    // Real body captured from an actual invalid (rotated-out) key during the same research -
    // allowance_refresh_timer is 0, the field TheGamesDbService uses to tell this apart from a
    // real rate limit. See ARCHITECTURE.md for the full record once this ships.
    private const string InvalidKeyJson = """{"code":403,"status":"Invalid API key was provided.","remaining_monthly_allowance":0,"allowance_refresh_timer":0}""";

    // A real rate-limit body was never actually observed (the key never hit its cap during
    // research) - this shape is inferred from the response schema (a real allowance_refresh_timer
    // countdown), not copied from a live response. See TheGamesDbService's own comment on the
    // heuristic this drives.
    private const string RateLimitedJson = """{"code":403,"status":"Query limit exceeded.","remaining_monthly_allowance":0,"allowance_refresh_timer":3600}""";

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private static (FakeLibraryRepository repo, FakeSettingsService settings, FakeImageCacheService images) CreateFakes(string? apiKey = "test-key")
    {
        var repo = new FakeLibraryRepository();
        repo.Platforms.Add(new Platform { Id = "snes", Name = "SNES", Extensions = ["sfc"] });

        var settings = new FakeSettingsService { TheGamesDbApiKey = apiKey };
        var images = new FakeImageCacheService();
        return (repo, settings, images);
    }

    private static Game AddGame(FakeLibraryRepository repo, string name = "Bahamut Lagoon")
    {
        var game = new Game { Id = Guid.NewGuid(), Path = $@"C:\roms\{name}.sfc", Name = name, PlatformId = "snes" };
        repo.Games.Add(game);
        return game;
    }

    // 1. No key configured.
    [Fact]
    public async Task FetchDescriptionAndScreenshotsAsync_NoApiKeyConfigured_ReturnsNoKeyConfiguredWithoutCallingHttp()
    {
        var (repo, settings, images) = CreateFakes(apiKey: null);
        var game = AddGame(repo);
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("HTTP should not be called"));
        var service = new TheGamesDbService(new HttpClient(handler), settings, repo, images, NullLogger<TheGamesDbService>.Instance);

        var outcome = await service.FetchDescriptionAndScreenshotsAsync(game);

        Assert.Equal(TheGamesDbOutcome.NoKeyConfigured, outcome);
        Assert.Empty(handler.Requests);
    }

    // 2. Not found.
    [Fact]
    public async Task FetchDescriptionAndScreenshotsAsync_GameNotFoundOnProvider_PersistsNotFoundAndReturnsNotFound()
    {
        var (repo, settings, images) = CreateFakes();
        var game = AddGame(repo, "Some Obscure Homebrew");
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, SearchNotFoundJson));
        var service = new TheGamesDbService(new HttpClient(handler), settings, repo, images, NullLogger<TheGamesDbService>.Instance);

        var outcome = await service.FetchDescriptionAndScreenshotsAsync(game);

        Assert.Equal(TheGamesDbOutcome.NotFound, outcome);
        var boxArt = Assert.Single(repo.BoxArtRecords);
        Assert.Equal(BoxArtStatus.NotFoundOnProvider, boxArt.DescriptionStatus);
        Assert.Null(boxArt.Description);
    }

    // 3. Rate limit exceeded.
    [Fact]
    public async Task FetchDescriptionAndScreenshotsAsync_RateLimitResponse_ReturnsRateLimitedAndPersistsResetTime()
    {
        var (repo, settings, images) = CreateFakes();
        var game = AddGame(repo);
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.Forbidden, RateLimitedJson));
        var service = new TheGamesDbService(new HttpClient(handler), settings, repo, images, NullLogger<TheGamesDbService>.Instance);

        var before = DateTime.UtcNow;
        var outcome = await service.FetchDescriptionAndScreenshotsAsync(game);

        Assert.Equal(TheGamesDbOutcome.RateLimited, outcome);
        var boxArt = Assert.Single(repo.BoxArtRecords);
        Assert.Equal(BoxArtStatus.FetchFailed, boxArt.DescriptionStatus);
        Assert.NotNull(boxArt.DescriptionRateLimitResetUtc);
        Assert.True(boxArt.DescriptionRateLimitResetUtc > before.AddSeconds(3500));
    }

    [Fact]
    public async Task FetchDescriptionAndScreenshotsAsync_InvalidApiKeyResponse_DistinctFromRateLimit_ReturnsFailedWithoutResetTime()
    {
        var (repo, settings, images) = CreateFakes();
        var game = AddGame(repo);
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.Forbidden, InvalidKeyJson));
        var service = new TheGamesDbService(new HttpClient(handler), settings, repo, images, NullLogger<TheGamesDbService>.Instance);

        var outcome = await service.FetchDescriptionAndScreenshotsAsync(game);

        Assert.Equal(TheGamesDbOutcome.Failed, outcome);
        var boxArt = Assert.Single(repo.BoxArtRecords);
        Assert.Equal(BoxArtStatus.FetchFailed, boxArt.DescriptionStatus);
        Assert.Null(boxArt.DescriptionRateLimitResetUtc);
    }

    // 4. Corrupted/malformed response.
    [Fact]
    public async Task FetchDescriptionAndScreenshotsAsync_CorruptedJsonResponse_ReturnsFailed()
    {
        var (repo, settings, images) = CreateFakes();
        var game = AddGame(repo);
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, "{ this is not valid json"));
        var service = new TheGamesDbService(new HttpClient(handler), settings, repo, images, NullLogger<TheGamesDbService>.Instance);

        var outcome = await service.FetchDescriptionAndScreenshotsAsync(game);

        Assert.Equal(TheGamesDbOutcome.Failed, outcome);
        Assert.Equal(BoxArtStatus.FetchFailed, Assert.Single(repo.BoxArtRecords).DescriptionStatus);
    }

    // 5. Cancellation.
    [Fact]
    public async Task FetchDescriptionAndScreenshotsAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var (repo, settings, images) = CreateFakes();
        var game = AddGame(repo);
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("HTTP should not be called"));
        var service = new TheGamesDbService(new HttpClient(handler), settings, repo, images, NullLogger<TheGamesDbService>.Instance);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.FetchDescriptionAndScreenshotsAsync(game, cts.Token));
    }

    // 6. Network failure.
    [Fact]
    public async Task FetchDescriptionAndScreenshotsAsync_NetworkFailure_ReturnsFailed()
    {
        var (repo, settings, images) = CreateFakes();
        var game = AddGame(repo);
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("Simulated network failure"));
        var service = new TheGamesDbService(new HttpClient(handler), settings, repo, images, NullLogger<TheGamesDbService>.Instance);

        var outcome = await service.FetchDescriptionAndScreenshotsAsync(game);

        Assert.Equal(TheGamesDbOutcome.Failed, outcome);
        Assert.Equal(BoxArtStatus.FetchFailed, Assert.Single(repo.BoxArtRecords).DescriptionStatus);
    }

    // Happy path, plus structural checks beyond the 6 required states.
    [Fact]
    public async Task FetchDescriptionAndScreenshotsAsync_SuccessfulLookup_PersistsDescriptionAndScreenshots()
    {
        var (repo, settings, images) = CreateFakes();
        var game = AddGame(repo);
        var handler = new FakeHttpMessageHandler(req => req.RequestUri!.AbsolutePath.Contains("/Games/ByGameName")
            ? JsonResponse(HttpStatusCode.OK, SearchFoundJson)
            : JsonResponse(HttpStatusCode.OK, ImagesFoundJson));
        var service = new TheGamesDbService(new HttpClient(handler), settings, repo, images, NullLogger<TheGamesDbService>.Instance);

        var outcome = await service.FetchDescriptionAndScreenshotsAsync(game);

        Assert.Equal(TheGamesDbOutcome.Cached, outcome);
        var boxArt = Assert.Single(repo.BoxArtRecords);
        Assert.Equal(BoxArtStatus.Cached, boxArt.DescriptionStatus);
        Assert.Equal("A tale of dragons and war.", boxArt.Description);
        // Only the 2 "screenshot"-typed entries, the "fanart" one is filtered out.
        Assert.Equal(2, boxArt.ScreenshotLocalPaths.Count);
        Assert.Contains("https://cdn.thegamesdb.net/images/original/screenshots/9054-1.jpg", images.RequestedUrls);
        Assert.Contains("https://cdn.thegamesdb.net/images/original/screenshots/9054-2.jpg", images.RequestedUrls);
        Assert.DoesNotContain("https://cdn.thegamesdb.net/images/original/fanart/9054-1.jpg", images.RequestedUrls);
    }

    [Fact]
    public async Task FetchDescriptionAndScreenshotsAsync_GameFoundButNoScreenshots_StillCachesDescription()
    {
        var (repo, settings, images) = CreateFakes();
        var game = AddGame(repo);
        var handler = new FakeHttpMessageHandler(req => req.RequestUri!.AbsolutePath.Contains("/Games/ByGameName")
            ? JsonResponse(HttpStatusCode.OK, SearchFoundJson)
            : JsonResponse(HttpStatusCode.OK, ImagesEmptyJson));
        var service = new TheGamesDbService(new HttpClient(handler), settings, repo, images, NullLogger<TheGamesDbService>.Instance);

        var outcome = await service.FetchDescriptionAndScreenshotsAsync(game);

        Assert.Equal(TheGamesDbOutcome.Cached, outcome);
        var boxArt = Assert.Single(repo.BoxArtRecords);
        Assert.Equal("A tale of dragons and war.", boxArt.Description);
        Assert.Empty(boxArt.ScreenshotLocalPaths);
    }

    [Fact]
    public async Task FetchDescriptionAndScreenshotsAsync_AlreadyCached_IsSkippedNotRefetched()
    {
        var (repo, settings, images) = CreateFakes();
        var game = AddGame(repo);
        repo.BoxArtRecords.Add(new BoxArt
        {
            Id = Guid.NewGuid(),
            GameId = game.Id,
            DescriptionStatus = BoxArtStatus.Cached,
            Description = "Already fetched."
        });
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("HTTP should not be called for an already-cached game"));
        var service = new TheGamesDbService(new HttpClient(handler), settings, repo, images, NullLogger<TheGamesDbService>.Instance);

        var outcome = await service.FetchDescriptionAndScreenshotsAsync(game);

        Assert.Equal(TheGamesDbOutcome.Cached, outcome);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task FetchDescriptionAndScreenshotsAsync_PreviouslyFetchFailed_IsRetried()
    {
        var (repo, settings, images) = CreateFakes();
        var game = AddGame(repo);
        repo.BoxArtRecords.Add(new BoxArt { Id = Guid.NewGuid(), GameId = game.Id, DescriptionStatus = BoxArtStatus.FetchFailed });
        var handler = new FakeHttpMessageHandler(req => req.RequestUri!.AbsolutePath.Contains("/Games/ByGameName")
            ? JsonResponse(HttpStatusCode.OK, SearchFoundJson)
            : JsonResponse(HttpStatusCode.OK, ImagesFoundJson));
        var service = new TheGamesDbService(new HttpClient(handler), settings, repo, images, NullLogger<TheGamesDbService>.Instance);

        var outcome = await service.FetchDescriptionAndScreenshotsAsync(game);

        Assert.Equal(TheGamesDbOutcome.Cached, outcome);
    }

    [Fact]
    public async Task FetchDescriptionAndScreenshotsAsync_ExistingBoxArtCoverArt_IsNotClobbered()
    {
        // TheGamesDbService and MetadataService share BoxArt - a description fetch must never
        // overwrite the SteamGridDB-owned cover-art fields already persisted there.
        var (repo, settings, images) = CreateFakes();
        var game = AddGame(repo);
        repo.BoxArtRecords.Add(new BoxArt
        {
            Id = Guid.NewGuid(),
            GameId = game.Id,
            Status = BoxArtStatus.Cached,
            LocalPath = @"C:\cache\cover.png",
            ReleaseYear = 1996
        });
        var handler = new FakeHttpMessageHandler(req => req.RequestUri!.AbsolutePath.Contains("/Games/ByGameName")
            ? JsonResponse(HttpStatusCode.OK, SearchFoundJson)
            : JsonResponse(HttpStatusCode.OK, ImagesFoundJson));
        var service = new TheGamesDbService(new HttpClient(handler), settings, repo, images, NullLogger<TheGamesDbService>.Instance);

        await service.FetchDescriptionAndScreenshotsAsync(game);

        var boxArt = Assert.Single(repo.BoxArtRecords);
        Assert.Equal(@"C:\cache\cover.png", boxArt.LocalPath);
        Assert.Equal(BoxArtStatus.Cached, boxArt.Status);
        Assert.Equal(1996, boxArt.ReleaseYear);
        Assert.Equal("A tale of dragons and war.", boxArt.Description);
    }

    [Fact]
    public async Task FetchDescriptionAndScreenshotsAsync_GenesisPlatform_FallsBackToMegaDriveIdWhenFirstIdHasNoMatch()
    {
        // Real finding from the metadata-source decision research: TheGamesDB catalogs the same
        // hardware under 2 separate platform ids (Sega Genesis=18, Sega Mega Drive=36) - a game
        // region-locked to the Mega Drive name wouldn't match under the Genesis id alone.
        var repo = new FakeLibraryRepository();
        repo.Platforms.Add(new Platform { Id = "genesis", Name = "Genesis", Extensions = ["md"] });
        var settings = new FakeSettingsService { TheGamesDbApiKey = "test-key" };
        var images = new FakeImageCacheService();
        var game = new Game { Id = Guid.NewGuid(), Path = @"C:\roms\pulseman.md", Name = "Pulseman", PlatformId = "genesis" };
        repo.Games.Add(game);

        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/Games/Images"))
            {
                return JsonResponse(HttpStatusCode.OK, ImagesEmptyJson);
            }

            // filter[platform]=18 (Genesis) misses, filter[platform]=36 (Mega Drive) hits.
            return req.RequestUri.Query.Contains("filter%5Bplatform%5D=36") || req.RequestUri.Query.Contains("filter[platform]=36")
                ? JsonResponse(HttpStatusCode.OK, SearchFoundJson)
                : JsonResponse(HttpStatusCode.OK, SearchNotFoundJson);
        });
        var service = new TheGamesDbService(new HttpClient(handler), settings, repo, images, NullLogger<TheGamesDbService>.Instance);

        var outcome = await service.FetchDescriptionAndScreenshotsAsync(game);

        Assert.Equal(TheGamesDbOutcome.Cached, outcome);
        var searchRequests = handler.Requests.Where(r => r.RequestUri!.AbsolutePath.Contains("/Games/ByGameName")).ToList();
        Assert.Equal(2, searchRequests.Count); // tried id 18 first, then fell back to 36
    }
}
