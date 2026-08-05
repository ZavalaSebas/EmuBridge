using EmuBridge.Models;
using EmuBridge.Services;
using EmuBridge.Tests.Services;
using EmuBridge.ViewModels;

namespace EmuBridge.Tests.ViewModels;

public class GameDetailViewModelTests
{
    private readonly FakeLibraryRepository _repository = new();
    private readonly FakeTheGamesDbService _theGamesDbService = new();
    private readonly GameDetailViewModel _viewModel;

    public GameDetailViewModelTests()
    {
        _repository.Platforms.Add(new Platform { Id = "nes", Name = "Nintendo Entertainment System", Extensions = ["nes"] });
        _viewModel = new GameDetailViewModel(_repository, _theGamesDbService);
    }

    private Game AddGame() => new() { Id = Guid.NewGuid(), Path = @"C:\roms\mario.nes", Name = "Super Mario Bros. 3", PlatformId = "nes" };

    [Fact]
    public void SetGame_SetsNameImmediately()
    {
        var game = AddGame();

        _viewModel.SetGame(game);

        Assert.Equal("Super Mario Bros. 3", _viewModel.Name);
    }

    [Fact]
    public async Task InitializeAsync_ResolvesPlatformNameFromRepository()
    {
        var game = AddGame();
        _viewModel.SetGame(game);

        await _viewModel.InitializeAsync();

        Assert.Equal("Nintendo Entertainment System", _viewModel.PlatformName);
    }

    [Fact]
    public async Task InitializeAsync_PlatformRowMissing_FallsBackToPlatformId()
    {
        var game = new Game { Id = Guid.NewGuid(), Path = @"C:\roms\x.zzz", Name = "Mystery Game", PlatformId = "ghost-platform" };
        _viewModel.SetGame(game);

        await _viewModel.InitializeAsync();

        Assert.Equal("ghost-platform", _viewModel.PlatformName);
    }

    [Fact]
    public async Task InitializeAsync_CachedBoxArt_SetsCoverImagePath()
    {
        var game = AddGame();
        _repository.BoxArtRecords.Add(new BoxArt { GameId = game.Id, Status = BoxArtStatus.Cached, LocalPath = @"C:\cache\a.png" });
        _viewModel.SetGame(game);

        await _viewModel.InitializeAsync();

        Assert.Equal(@"C:\cache\a.png", _viewModel.CoverImagePath);
    }

    [Fact]
    public async Task InitializeAsync_NoBoxArt_CoverImagePathIsNull()
    {
        var game = AddGame();
        _viewModel.SetGame(game);

        await _viewModel.InitializeAsync();

        Assert.Null(_viewModel.CoverImagePath);
    }

    [Fact]
    public async Task InitializeAsync_BoxArtWithReleaseYear_SetsReleaseYearText()
    {
        var game = AddGame();
        _repository.BoxArtRecords.Add(new BoxArt { GameId = game.Id, Status = BoxArtStatus.Cached, ReleaseYear = 1990 });
        _viewModel.SetGame(game);

        await _viewModel.InitializeAsync();

        Assert.Equal("Release year: 1990", _viewModel.ReleaseYearText);
    }

    [Fact]
    public async Task InitializeAsync_BoxArtWithoutReleaseYear_ShowsUnknown()
    {
        var game = AddGame();
        _repository.BoxArtRecords.Add(new BoxArt { GameId = game.Id, Status = BoxArtStatus.Cached, ReleaseYear = null });
        _viewModel.SetGame(game);

        await _viewModel.InitializeAsync();

        Assert.Equal("Release year: unknown", _viewModel.ReleaseYearText);
    }

    [Fact]
    public async Task InitializeAsync_NoBoxArtAtAll_ShowsUnknownReleaseYear()
    {
        var game = AddGame();
        _viewModel.SetGame(game);

        await _viewModel.InitializeAsync();

        Assert.Equal("Release year: unknown", _viewModel.ReleaseYearText);
    }

    [Fact]
    public async Task InitializeAsync_NoGameSet_DoesNotThrow()
    {
        await _viewModel.InitializeAsync();

        Assert.Equal(string.Empty, _viewModel.Name);
    }

    [Fact]
    public async Task InitializeAsync_TheGamesDbCachedWithDescription_ShowsRealDescriptionText()
    {
        var game = AddGame();
        _repository.BoxArtRecords.Add(new BoxArt { GameId = game.Id, DescriptionStatus = BoxArtStatus.Cached, Description = "A grand adventure." });
        _theGamesDbService.NextOutcome = TheGamesDbOutcome.Cached;
        _viewModel.SetGame(game);

        await _viewModel.InitializeAsync();

        Assert.Equal("Description: A grand adventure.", _viewModel.DescriptionText);
    }

    [Theory]
    [InlineData(TheGamesDbOutcome.NotFound)]
    [InlineData(TheGamesDbOutcome.NoKeyConfigured)]
    [InlineData(TheGamesDbOutcome.Failed)]
    public async Task InitializeAsync_TheGamesDbOutcomeWithNoDescription_ShowsNotAvailable(TheGamesDbOutcome outcome)
    {
        var game = AddGame();
        _theGamesDbService.NextOutcome = outcome;
        _viewModel.SetGame(game);

        await _viewModel.InitializeAsync();

        Assert.Equal("Description: not available", _viewModel.DescriptionText);
    }

    [Fact]
    public async Task InitializeAsync_TheGamesDbRateLimited_ShowsResetTimeInDescription()
    {
        var game = AddGame();
        var resetUtc = DateTime.UtcNow.AddHours(1);
        _repository.BoxArtRecords.Add(new BoxArt { GameId = game.Id, DescriptionStatus = BoxArtStatus.FetchFailed, DescriptionRateLimitResetUtc = resetUtc });
        _theGamesDbService.NextOutcome = TheGamesDbOutcome.RateLimited;
        _viewModel.SetGame(game);

        await _viewModel.InitializeAsync();

        Assert.Contains("rate limit reached", _viewModel.DescriptionText);
        Assert.Contains(resetUtc.ToLocalTime().ToString("t"), _viewModel.DescriptionText);
    }

    [Fact]
    public async Task InitializeAsync_TheGamesDbCachedWithScreenshots_PopulatesScreenshotPaths()
    {
        var game = AddGame();
        _repository.BoxArtRecords.Add(new BoxArt
        {
            GameId = game.Id,
            DescriptionStatus = BoxArtStatus.Cached,
            ScreenshotLocalPaths = [@"C:\cache\shot1.jpg", @"C:\cache\shot2.jpg"]
        });
        _theGamesDbService.NextOutcome = TheGamesDbOutcome.Cached;
        _viewModel.SetGame(game);

        await _viewModel.InitializeAsync();

        Assert.Equal(2, _viewModel.ScreenshotPaths.Count);
        Assert.True(_viewModel.HasScreenshots);
    }

    [Fact]
    public async Task InitializeAsync_NoScreenshots_HasScreenshotsIsFalse()
    {
        var game = AddGame();
        _viewModel.SetGame(game);

        await _viewModel.InitializeAsync();

        Assert.Empty(_viewModel.ScreenshotPaths);
        Assert.False(_viewModel.HasScreenshots);
    }

    [Fact]
    public async Task InitializeAsync_TheGamesDbServiceThrowsOperationCanceled_Propagates()
    {
        var game = AddGame();
        _theGamesDbService.ThrowOperationCanceled = true;
        _viewModel.SetGame(game);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _viewModel.InitializeAsync());
    }

    [Fact]
    public async Task InitializeAsync_CallsTheGamesDbServiceForTheSetGame()
    {
        var game = AddGame();
        _theGamesDbService.NextOutcome = TheGamesDbOutcome.NotFound;
        _viewModel.SetGame(game);

        await _viewModel.InitializeAsync();

        Assert.Contains(game.Id, _theGamesDbService.CalledForGameIds);
    }
}
