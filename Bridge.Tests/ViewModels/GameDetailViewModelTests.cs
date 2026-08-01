using Bridge.Models;
using Bridge.Tests.Services;
using Bridge.ViewModels;

namespace Bridge.Tests.ViewModels;

public class GameDetailViewModelTests
{
    private readonly FakeLibraryRepository _repository = new();
    private readonly GameDetailViewModel _viewModel;

    public GameDetailViewModelTests()
    {
        _repository.Platforms.Add(new Platform { Id = "nes", Name = "Nintendo Entertainment System", Extensions = ["nes"] });
        _viewModel = new GameDetailViewModel(_repository);
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
}
