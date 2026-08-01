using System.Net;
using System.Net.Http;
using Bridge.Models;
using Bridge.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bridge.Tests.Services;

public class MetadataServiceTests
{
    private const string SearchFoundJson = """{"success":true,"data":[{"id":123,"name":"Super Mario World"}]}""";
    private const string SearchFoundWithReleaseDateJson = """{"success":true,"data":[{"id":123,"name":"Super Mario World","release_date":774835200}]}""";
    private const string SearchFoundZeroReleaseDateJson = """{"success":true,"data":[{"id":123,"name":"Super Mario World","release_date":0}]}""";
    private const string SearchNotFoundJson = """{"success":true,"data":[]}""";
    private const string GridsFoundJson = """{"success":true,"data":[{"id":456,"url":"https://cdn.example.com/grid1.png"}]}""";
    private const string GridsNotFoundJson = """{"success":true,"data":[]}""";

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private static (FakeLibraryRepository repo, FakeSettingsService settings, FakeImageCacheService images) CreateFakes(string? apiKey = "test-key")
    {
        var repo = new FakeLibraryRepository();
        repo.Platforms.Add(new Platform { Id = "nes", Name = "NES", Extensions = ["nes"] });

        var settings = new FakeSettingsService { ApiKey = apiKey };
        var images = new FakeImageCacheService();
        return (repo, settings, images);
    }

    private static Game AddGame(FakeLibraryRepository repo, string name)
    {
        var game = new Game { Id = Guid.NewGuid(), Path = $@"C:\roms\{name}.nes", Name = name, PlatformId = "nes" };
        repo.Games.Add(game);
        return game;
    }

    [Fact]
    public async Task FetchMissingBoxArtAsync_NoApiKeyConfigured_ReturnsEmptyResultWithoutCallingHttp()
    {
        var (repo, settings, images) = CreateFakes(apiKey: null);
        AddGame(repo, "Super Mario World");
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("HTTP should not be called"));
        var service = new MetadataService(new HttpClient(handler), settings, repo, images, NullLogger<MetadataService>.Instance);

        var result = await service.FetchMissingBoxArtAsync(100, 150);

        Assert.Equal(0, result.Fetched);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task FetchMissingBoxArtAsync_SuccessfulLookup_PersistsCachedBoxArt()
    {
        var (repo, settings, images) = CreateFakes();
        var game = AddGame(repo, "Super Mario World");
        var handler = new FakeHttpMessageHandler(req => req.RequestUri!.AbsolutePath.Contains("/search/")
            ? JsonResponse(HttpStatusCode.OK, SearchFoundJson)
            : JsonResponse(HttpStatusCode.OK, GridsFoundJson));
        var service = new MetadataService(new HttpClient(handler), settings, repo, images, NullLogger<MetadataService>.Instance);

        var result = await service.FetchMissingBoxArtAsync(100, 150);

        Assert.Equal(1, result.Fetched);
        var boxArt = Assert.Single(repo.BoxArtRecords);
        Assert.Equal(game.Id, boxArt.GameId);
        Assert.Equal(BoxArtStatus.Cached, boxArt.Status);
        Assert.NotNull(boxArt.LocalPath);
        Assert.Equal(["https://cdn.example.com/grid1.png"], images.RequestedUrls);
    }

    [Fact]
    public async Task FetchMissingBoxArtAsync_SearchResultHasReleaseDate_PersistsReleaseYear()
    {
        var (repo, settings, images) = CreateFakes();
        AddGame(repo, "Super Mario World");
        // 774835200 = some 1994 date (Unix seconds) — the exact day doesn't matter, only that it
        // falls in 1994, to verify the Unix-seconds-to-year conversion end to end.
        var handler = new FakeHttpMessageHandler(req => req.RequestUri!.AbsolutePath.Contains("/search/")
            ? JsonResponse(HttpStatusCode.OK, SearchFoundWithReleaseDateJson)
            : JsonResponse(HttpStatusCode.OK, GridsFoundJson));
        var service = new MetadataService(new HttpClient(handler), settings, repo, images, NullLogger<MetadataService>.Instance);

        await service.FetchMissingBoxArtAsync(100, 150);

        Assert.Equal(1994, Assert.Single(repo.BoxArtRecords).ReleaseYear);
    }

    [Fact]
    public async Task FetchMissingBoxArtAsync_SearchResultHasNoReleaseDate_ReleaseYearIsNull()
    {
        var (repo, settings, images) = CreateFakes();
        AddGame(repo, "Super Mario World");
        var handler = new FakeHttpMessageHandler(req => req.RequestUri!.AbsolutePath.Contains("/search/")
            ? JsonResponse(HttpStatusCode.OK, SearchFoundJson)
            : JsonResponse(HttpStatusCode.OK, GridsFoundJson));
        var service = new MetadataService(new HttpClient(handler), settings, repo, images, NullLogger<MetadataService>.Instance);

        await service.FetchMissingBoxArtAsync(100, 150);

        Assert.Null(Assert.Single(repo.BoxArtRecords).ReleaseYear);
    }

    [Fact]
    public async Task FetchMissingBoxArtAsync_SearchResultHasZeroReleaseDate_ReleaseYearIsNull()
    {
        var (repo, settings, images) = CreateFakes();
        AddGame(repo, "Super Mario World");
        var handler = new FakeHttpMessageHandler(req => req.RequestUri!.AbsolutePath.Contains("/search/")
            ? JsonResponse(HttpStatusCode.OK, SearchFoundZeroReleaseDateJson)
            : JsonResponse(HttpStatusCode.OK, GridsFoundJson));
        var service = new MetadataService(new HttpClient(handler), settings, repo, images, NullLogger<MetadataService>.Instance);

        await service.FetchMissingBoxArtAsync(100, 150);

        Assert.Null(Assert.Single(repo.BoxArtRecords).ReleaseYear);
    }

    [Fact]
    public async Task FetchMissingBoxArtAsync_SearchReturnsNoResults_PersistsNotFoundOnProvider()
    {
        var (repo, settings, images) = CreateFakes();
        AddGame(repo, "Some Obscure Homebrew");
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, SearchNotFoundJson));
        var service = new MetadataService(new HttpClient(handler), settings, repo, images, NullLogger<MetadataService>.Instance);

        var result = await service.FetchMissingBoxArtAsync(100, 150);

        Assert.Equal(1, result.NotFound);
        Assert.Equal(BoxArtStatus.NotFoundOnProvider, Assert.Single(repo.BoxArtRecords).Status);
    }

    [Fact]
    public async Task FetchMissingBoxArtAsync_GameAlreadyCached_IsSkippedNotRefetched()
    {
        var (repo, settings, images) = CreateFakes();
        var game = AddGame(repo, "Super Mario World");
        repo.BoxArtRecords.Add(new BoxArt { Id = Guid.NewGuid(), GameId = game.Id, Status = BoxArtStatus.Cached, LocalPath = @"C:\cache\already.png" });
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("HTTP should not be called for an already-cached game"));
        var service = new MetadataService(new HttpClient(handler), settings, repo, images, NullLogger<MetadataService>.Instance);

        var result = await service.FetchMissingBoxArtAsync(100, 150);

        Assert.Equal(0, result.Fetched);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task FetchMissingBoxArtAsync_GameAlreadyNotFoundOnProvider_IsSkippedNotRetried()
    {
        var (repo, settings, images) = CreateFakes();
        var game = AddGame(repo, "Some Obscure Homebrew");
        repo.BoxArtRecords.Add(new BoxArt { Id = Guid.NewGuid(), GameId = game.Id, Status = BoxArtStatus.NotFoundOnProvider });
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("HTTP should not be called for a terminal not-found game"));
        var service = new MetadataService(new HttpClient(handler), settings, repo, images, NullLogger<MetadataService>.Instance);

        var result = await service.FetchMissingBoxArtAsync(100, 150);

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task FetchMissingBoxArtAsync_GameWithFetchFailedStatus_IsRetried()
    {
        var (repo, settings, images) = CreateFakes();
        var game = AddGame(repo, "Super Mario World");
        repo.BoxArtRecords.Add(new BoxArt { Id = Guid.NewGuid(), GameId = game.Id, Status = BoxArtStatus.FetchFailed });
        var handler = new FakeHttpMessageHandler(req => req.RequestUri!.AbsolutePath.Contains("/search/")
            ? JsonResponse(HttpStatusCode.OK, SearchFoundJson)
            : JsonResponse(HttpStatusCode.OK, GridsFoundJson));
        var service = new MetadataService(new HttpClient(handler), settings, repo, images, NullLogger<MetadataService>.Instance);

        var result = await service.FetchMissingBoxArtAsync(100, 150);

        Assert.Equal(1, result.Fetched);
        Assert.Equal(BoxArtStatus.Cached, Assert.Single(repo.BoxArtRecords).Status);
    }

    [Fact]
    public async Task FetchMissingBoxArtAsync_RateLimitResponse_StopsBatchEarly()
    {
        var (repo, settings, images) = CreateFakes();
        AddGame(repo, "First Game");
        AddGame(repo, "Second Game");
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var service = new MetadataService(new HttpClient(handler), settings, repo, images, NullLogger<MetadataService>.Instance);

        var result = await service.FetchMissingBoxArtAsync(100, 150);

        Assert.True(result.StoppedEarlyDueToRateLimit);
        Assert.Equal(1, result.Failed);
        Assert.Single(handler.Requests); // second game never attempted
    }

    [Fact]
    public async Task FetchMissingBoxArtAsync_AuthFailureResponse_StopsBatchEarly()
    {
        var (repo, settings, images) = CreateFakes();
        AddGame(repo, "First Game");
        AddGame(repo, "Second Game");
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var service = new MetadataService(new HttpClient(handler), settings, repo, images, NullLogger<MetadataService>.Instance);

        var result = await service.FetchMissingBoxArtAsync(100, 150);

        Assert.Equal(1, result.Failed);
        Assert.Single(handler.Requests); // second game never attempted
    }

    [Fact]
    public async Task FetchMissingBoxArtAsync_GameNameWithTags_NormalizesBeforeSearching()
    {
        var (repo, settings, images) = CreateFakes();
        AddGame(repo, "Super Mario World (USA) (Rev 1)");
        var handler = new FakeHttpMessageHandler(req => req.RequestUri!.AbsolutePath.Contains("/search/")
            ? JsonResponse(HttpStatusCode.OK, SearchFoundJson)
            : JsonResponse(HttpStatusCode.OK, GridsFoundJson));
        var service = new MetadataService(new HttpClient(handler), settings, repo, images, NullLogger<MetadataService>.Instance);

        await service.FetchMissingBoxArtAsync(100, 150);

        var searchRequest = Assert.Single(handler.Requests, r => r.RequestUri!.AbsolutePath.Contains("/search/"));
        var searchPath = Uri.UnescapeDataString(searchRequest.RequestUri!.AbsolutePath);
        Assert.Contains("Super Mario World", searchPath);
        Assert.DoesNotContain("USA", searchPath);
        Assert.DoesNotContain("Rev", searchPath);
    }
}
