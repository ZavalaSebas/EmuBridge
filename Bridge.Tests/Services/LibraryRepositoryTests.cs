using System.IO;
using Bridge.Models;
using Bridge.Services;
using LiteDB;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bridge.Tests.Services;

public class LibraryRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private LibraryRepository _repository;

    public LibraryRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bridge_test_{Guid.NewGuid()}.db");
        _repository = new LibraryRepository(_dbPath, NullLogger<LibraryRepository>.Instance);
    }

    public void Dispose()
    {
        _repository.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public async Task Constructor_OnFreshDatabase_SeedsUnknownPlatformSentinel()
    {
        var platforms = await _repository.GetPlatformsAsync();

        var unknown = platforms.SingleOrDefault(p => p.Id == Config.UnknownPlatformId);
        Assert.NotNull(unknown);
        Assert.Empty(unknown.Extensions);
    }

    [Fact]
    public async Task Constructor_OnFreshDatabase_SeedsFifteenBuiltInPlatforms()
    {
        var platforms = await _repository.GetPlatformsAsync();

        Assert.Equal(16, platforms.Count); // 15 built-in + the unknown sentinel
    }

    [Fact]
    public async Task Constructor_OnAlreadySeededDatabase_DoesNotReseed()
    {
        var firstOpenCount = (await _repository.GetPlatformsAsync()).Count;
        _repository.Dispose();

        using var reopened = new LibraryRepository(_dbPath, NullLogger<LibraryRepository>.Instance);
        var secondOpenCount = (await reopened.GetPlatformsAsync()).Count;

        Assert.Equal(firstOpenCount, secondOpenCount);

        // Prevent the class-level teardown from disposing an already-disposed instance.
        _repository = reopened;
    }

    [Fact]
    public async Task UpsertGameAsync_NewGame_IsAdded()
    {
        var game = new Game { Id = Guid.NewGuid(), Path = @"C:\roms\mario.nes", Name = "mario", PlatformId = "nes" };

        await _repository.UpsertGameAsync(game);
        var all = await _repository.GetAllGamesAsync();

        var stored = Assert.Single(all);
        Assert.Equal(game.Path, stored.Path);
    }

    [Fact]
    public async Task UpsertGameAsync_ExistingGameId_UpdatesWithoutDuplicating()
    {
        var game = new Game { Id = Guid.NewGuid(), Path = @"C:\roms\mario.nes", Name = "mario", PlatformId = "unknown" };
        await _repository.UpsertGameAsync(game);

        game.PlatformId = "nes";
        await _repository.UpsertGameAsync(game);

        var all = await _repository.GetAllGamesAsync();
        var stored = Assert.Single(all);
        Assert.Equal("nes", stored.PlatformId);
    }

    [Fact]
    public async Task UpsertGameAsync_DuplicatePathDifferentId_ThrowsDueToUniqueIndex()
    {
        var first = new Game { Id = Guid.NewGuid(), Path = @"C:\roms\mario.nes", Name = "mario", PlatformId = "nes" };
        await _repository.UpsertGameAsync(first);

        var second = new Game { Id = Guid.NewGuid(), Path = @"C:\roms\mario.nes", Name = "mario-dup", PlatformId = "nes" };

        await Assert.ThrowsAsync<LiteException>(() => _repository.UpsertGameAsync(second));
    }

    [Fact]
    public async Task MarkGamesMissingAsync_SetsIsMissingTrue()
    {
        var game = new Game { Id = Guid.NewGuid(), Path = @"C:\roms\mario.nes", Name = "mario", PlatformId = "nes" };
        await _repository.UpsertGameAsync(game);

        await _repository.MarkGamesMissingAsync([game.Id]);

        var all = await _repository.GetAllGamesAsync();
        Assert.True(Assert.Single(all).IsMissing);
    }

    [Fact]
    public async Task AddScanFolderAsync_PersistsFolder()
    {
        var folder = new ScanFolder { Id = Guid.NewGuid(), Path = @"C:\roms" };

        await _repository.AddScanFolderAsync(folder);
        var all = await _repository.GetScanFoldersAsync();

        var stored = Assert.Single(all);
        Assert.Equal(folder.Path, stored.Path);
    }

    [Fact]
    public async Task GetBoxArtAsync_NoRecordForGame_ReturnsNull()
    {
        var boxArt = await _repository.GetBoxArtAsync(Guid.NewGuid());

        Assert.Null(boxArt);
    }

    [Fact]
    public async Task UpsertBoxArtAsync_NewRecord_IsAdded()
    {
        var gameId = Guid.NewGuid();
        var boxArt = new BoxArt { GameId = gameId, Status = BoxArtStatus.Cached, LocalPath = @"C:\cache\a.png" };

        await _repository.UpsertBoxArtAsync(boxArt);
        var stored = await _repository.GetBoxArtAsync(gameId);

        Assert.NotNull(stored);
        Assert.Equal(BoxArtStatus.Cached, stored.Status);
        Assert.Equal(@"C:\cache\a.png", stored.LocalPath);
    }

    [Fact]
    public async Task UpsertBoxArtAsync_ExistingGameId_UpdatesWithoutDuplicating()
    {
        var gameId = Guid.NewGuid();
        await _repository.UpsertBoxArtAsync(new BoxArt { GameId = gameId, Status = BoxArtStatus.FetchFailed });

        await _repository.UpsertBoxArtAsync(new BoxArt { GameId = gameId, Status = BoxArtStatus.Cached, LocalPath = @"C:\cache\a.png" });

        var stored = await _repository.GetBoxArtAsync(gameId);
        Assert.Equal(BoxArtStatus.Cached, stored!.Status);
    }
}
