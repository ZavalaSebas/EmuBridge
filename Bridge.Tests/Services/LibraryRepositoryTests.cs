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
    public async Task DeleteGameAsync_ExistingGame_RemovesIt()
    {
        var game = new Game { Id = Guid.NewGuid(), Path = @"C:\roms\mario.nes", Name = "mario", PlatformId = "nes" };
        await _repository.UpsertGameAsync(game);

        await _repository.DeleteGameAsync(game.Id);

        Assert.Empty(await _repository.GetAllGamesAsync());
    }

    [Fact]
    public async Task DeleteGameAsync_NonexistentId_NoOp()
    {
        var game = new Game { Id = Guid.NewGuid(), Path = @"C:\roms\mario.nes", Name = "mario", PlatformId = "nes" };
        await _repository.UpsertGameAsync(game);

        await _repository.DeleteGameAsync(Guid.NewGuid());

        Assert.Single(await _repository.GetAllGamesAsync());
    }

    [Fact]
    public async Task DeleteBoxArtAsync_ExistingBoxArt_RemovesIt()
    {
        var gameId = Guid.NewGuid();
        await _repository.UpsertBoxArtAsync(new BoxArt { GameId = gameId, Status = BoxArtStatus.Cached, LocalPath = @"C:\cache\a.png" });

        await _repository.DeleteBoxArtAsync(gameId);

        Assert.Null(await _repository.GetBoxArtAsync(gameId));
    }

    [Fact]
    public async Task DeleteBoxArtAsync_NoBoxArtForGame_NoOp()
    {
        // Should not throw when there's nothing to delete — same silent-skip idiom as
        // MarkGamesMissingAsync/DeleteGameAsync for an unmatched id.
        await _repository.DeleteBoxArtAsync(Guid.NewGuid());
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

    [Fact]
    public async Task UpsertEmulatorAsync_NewExecutablePath_IsAdded()
    {
        var emulator = new Emulator { Name = "RetroArch", ExecutablePath = @"C:\emu\retroarch.exe", InstallSource = InstallSource.UserProvided };

        var stored = await _repository.UpsertEmulatorAsync(emulator);

        Assert.NotEqual(Guid.Empty, stored.Id);
        var fetched = await _repository.GetEmulatorByExecutablePathAsync(@"C:\emu\retroarch.exe");
        Assert.NotNull(fetched);
        Assert.Equal(stored.Id, fetched!.Id);
    }

    [Fact]
    public async Task UpsertEmulatorAsync_SameExecutablePathTwice_ReusesSameId()
    {
        var first = await _repository.UpsertEmulatorAsync(new Emulator { Name = "RetroArch", ExecutablePath = @"C:\emu\retroarch.exe", InstallSource = InstallSource.UserProvided });
        var second = await _repository.UpsertEmulatorAsync(new Emulator { Name = "RetroArch Updated", ExecutablePath = @"C:\emu\retroarch.exe", InstallSource = InstallSource.UserProvided });

        Assert.Equal(first.Id, second.Id);
        var fetched = await _repository.GetEmulatorByExecutablePathAsync(@"C:\emu\retroarch.exe");
        Assert.Equal("RetroArch Updated", fetched!.Name);
    }

    [Fact]
    public async Task GetEmulatorByKnownEmulatorIdAsync_NoMatch_ReturnsNull()
    {
        var result = await _repository.GetEmulatorByKnownEmulatorIdAsync("retroarch");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetEmulatorByKnownEmulatorIdAsync_MatchExists_ReturnsIt()
    {
        var stored = await _repository.UpsertEmulatorAsync(new Emulator
        {
            KnownEmulatorId = "retroarch",
            Name = "RetroArch",
            ExecutablePath = @"C:\emu\retroarch.exe",
            InstallSource = InstallSource.BridgeManaged
        });

        var fetched = await _repository.GetEmulatorByKnownEmulatorIdAsync("retroarch");

        Assert.NotNull(fetched);
        Assert.Equal(stored.Id, fetched!.Id);
    }

    [Fact]
    public async Task UpsertEmulatorProfileAsync_ExistingPlatformId_UpdatesWithoutDuplicating()
    {
        var emulator = await _repository.UpsertEmulatorAsync(new Emulator { Name = "RetroArch", ExecutablePath = @"C:\emu\retroarch.exe", InstallSource = InstallSource.UserProvided });
        await _repository.UpsertEmulatorProfileAsync(new EmulatorProfile { EmulatorId = emulator.Id, PlatformId = "nes", ArgumentTemplate = "\"{RomPath}\"" });

        await _repository.UpsertEmulatorProfileAsync(new EmulatorProfile { EmulatorId = emulator.Id, PlatformId = "nes", ArgumentTemplate = "-L nestopia.dll \"{RomPath}\"" });

        var profile = await _repository.GetEmulatorProfileByPlatformIdAsync("nes");
        Assert.Equal("-L nestopia.dll \"{RomPath}\"", profile!.ArgumentTemplate);
    }

    // The whole point of ADR-11's migration: existing Phase 1 EmulatorConfig data must survive
    // the upgrade to Emulator/EmulatorProfile without the user having to reconfigure anything.
    [Fact]
    public async Task Constructor_LegacyEmulatorConfigsPresent_MigratesToEmulatorAndProfile()
    {
        _repository.Dispose();

        using (var db = new LiteDatabase(_dbPath))
        {
            db.GetCollection<LegacyEmulatorConfigForTest>("emulatorConfigs").Insert(new LegacyEmulatorConfigForTest
            {
                Id = Guid.NewGuid(),
                PlatformId = "nes",
                Name = "Old NES Emulator",
                ExecutablePath = @"C:\emu\nes.exe",
                ArgumentTemplate = "\"{RomPath}\""
            });
        }

        var migrated = new LibraryRepository(_dbPath, NullLogger<LibraryRepository>.Instance);

        var profile = await migrated.GetEmulatorProfileByPlatformIdAsync("nes");
        Assert.NotNull(profile);

        var emulator = await migrated.GetEmulatorByIdAsync(profile!.EmulatorId);
        Assert.NotNull(emulator);
        Assert.Equal(@"C:\emu\nes.exe", emulator!.ExecutablePath);
        Assert.Equal(InstallSource.UserProvided, emulator.InstallSource);

        _repository = migrated;
    }

    // The actual scenario that motivated ReconcileSeedPlatformExtensions: a real, already-seeded
    // bridge.db (like every existing user's) whose atari2600 row still only has the pre-fix
    // extension list. SeedPlatformsIfEmpty is one-shot and would never touch this row again on
    // its own — confirmed by reading the code, not assumed. This test proves the reconciliation
    // actually reaches pre-existing data, not just fresh databases.
    [Fact]
    public async Task Constructor_PreExistingPlatformMissingNewSeedExtension_AddsItWithoutLosingExisting()
    {
        _repository.Dispose();

        using (var db = new LiteDatabase(_dbPath))
        {
            var platforms = db.GetCollection<Platform>("platforms");
            var atari2600 = platforms.FindById("atari2600");
            atari2600.Extensions = ["a26"]; // simulates the pre-fix seed, before "bin" was added
            platforms.Update(atari2600);
        }

        var reconciled = new LibraryRepository(_dbPath, NullLogger<LibraryRepository>.Instance);

        var platform = (await reconciled.GetPlatformsAsync()).Single(p => p.Id == "atari2600");
        Assert.Contains("a26", platform.Extensions);
        Assert.Contains("bin", platform.Extensions);

        _repository = reconciled;
    }

    [Fact]
    public async Task Constructor_PreExistingRowHasCustomExtensionNotInSeed_IsPreserved()
    {
        _repository.Dispose();

        using (var db = new LiteDatabase(_dbPath))
        {
            var platforms = db.GetCollection<Platform>("platforms");
            var atari2600 = platforms.FindById("atari2600");
            atari2600.Extensions = ["a26", "custom-user-ext"];
            platforms.Update(atari2600);
        }

        var reconciled = new LibraryRepository(_dbPath, NullLogger<LibraryRepository>.Instance);

        var platform = (await reconciled.GetPlatformsAsync()).Single(p => p.Id == "atari2600");
        Assert.Contains("custom-user-ext", platform.Extensions);
        Assert.Contains("bin", platform.Extensions);

        _repository = reconciled;
    }

    [Fact]
    public async Task Constructor_SeedPlatformRowMissingEntirely_IsReinserted()
    {
        _repository.Dispose();

        using (var db = new LiteDatabase(_dbPath))
        {
            db.GetCollection<Platform>("platforms").Delete("atari2600");
        }

        var reconciled = new LibraryRepository(_dbPath, NullLogger<LibraryRepository>.Instance);

        var platform = (await reconciled.GetPlatformsAsync()).SingleOrDefault(p => p.Id == "atari2600");
        Assert.NotNull(platform);
        Assert.Contains("bin", platform!.Extensions);

        _repository = reconciled;
    }

    private class LegacyEmulatorConfigForTest
    {
        public Guid Id { get; set; }
        public string PlatformId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public string ArgumentTemplate { get; set; } = string.Empty;
    }
}
