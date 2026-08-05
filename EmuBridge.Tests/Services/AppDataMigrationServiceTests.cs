using System.IO;
using EmuBridge.Models;
using EmuBridge.Services;
using LiteDB;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmuBridge.Tests.Services;

public class AppDataMigrationServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _oldPath;
    private readonly string _newPath;

    public AppDataMigrationServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"emubridge_migration_test_{Guid.NewGuid()}");
        _oldPath = Path.Combine(_root, "Bridge");
        _newPath = Path.Combine(_root, "EmuBridge");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void MigrateIfNeeded_NeitherFolderExists_FreshInstall_DoesNothing()
    {
        var service = new AppDataMigrationService(_oldPath, _newPath, NullLogger<AppDataMigrationService>.Instance);

        service.MigrateIfNeeded();

        Assert.False(Directory.Exists(_oldPath));
        Assert.False(Directory.Exists(_newPath));
    }

    [Fact]
    public void MigrateIfNeeded_OldFolderExistsNewDoesNot_MovesTheWholeFolder()
    {
        Directory.CreateDirectory(_oldPath);
        File.WriteAllText(Path.Combine(_oldPath, "settings.json"), "{}");
        var service = new AppDataMigrationService(_oldPath, _newPath, NullLogger<AppDataMigrationService>.Instance);

        service.MigrateIfNeeded();

        Assert.False(Directory.Exists(_oldPath));
        Assert.True(Directory.Exists(_newPath));
        Assert.True(File.Exists(Path.Combine(_newPath, "settings.json")));
    }

    [Fact]
    public void MigrateIfNeeded_OldFolderHasBridgeDb_RenamesToEmuBridgeDb()
    {
        // A real LiteDB file, not a plain-text stand-in - RewriteStoredAbsolutePaths genuinely
        // opens this file with LiteDB now, so a fake non-LiteDB file would just get overwritten
        // with an empty valid database instead of exercising the real rename path.
        Directory.CreateDirectory(_oldPath);
        using (var db = new LiteDatabase(Path.Combine(_oldPath, "bridge.db")))
        {
            db.GetCollection<BoxArt>("boxArt").Insert(new BoxArt { Id = Guid.NewGuid(), GameId = Guid.NewGuid() });
        }
        var service = new AppDataMigrationService(_oldPath, _newPath, NullLogger<AppDataMigrationService>.Instance);

        service.MigrateIfNeeded();

        Assert.False(File.Exists(Path.Combine(_newPath, "bridge.db")));
        var migratedDb = Path.Combine(_newPath, "emubridge.db");
        Assert.True(File.Exists(migratedDb));
        using var reopened = new LiteDatabase(migratedDb);
        Assert.Single(reopened.GetCollection<BoxArt>("boxArt").FindAll());
    }

    [Fact]
    public void MigrateIfNeeded_OldFolderHasNoBridgeDb_DoesNotThrow()
    {
        // A fresh-ish install that never got far enough to create bridge.db (e.g. only
        // settings.json exists) shouldn't fail the migration over a file that was never there.
        Directory.CreateDirectory(_oldPath);
        File.WriteAllText(Path.Combine(_oldPath, "settings.json"), "{}");
        var service = new AppDataMigrationService(_oldPath, _newPath, NullLogger<AppDataMigrationService>.Instance);

        service.MigrateIfNeeded();

        Assert.True(Directory.Exists(_newPath));
        Assert.False(File.Exists(Path.Combine(_newPath, "emubridge.db")));
    }

    [Fact]
    public void MigrateIfNeeded_NewFolderAlreadyExists_DoesNotTouchEitherFolder()
    {
        // Already migrated (or a fresh EmuBridge-only install that happens to share this temp
        // root with a leftover old folder) - never silently overwrite already-current data.
        Directory.CreateDirectory(_oldPath);
        File.WriteAllText(Path.Combine(_oldPath, "settings.json"), "old-data");
        Directory.CreateDirectory(_newPath);
        File.WriteAllText(Path.Combine(_newPath, "settings.json"), "new-data");
        var service = new AppDataMigrationService(_oldPath, _newPath, NullLogger<AppDataMigrationService>.Instance);

        service.MigrateIfNeeded();

        Assert.True(Directory.Exists(_oldPath));
        Assert.Equal("new-data", File.ReadAllText(Path.Combine(_newPath, "settings.json")));
    }

    [Fact]
    public void MigrateIfNeeded_CalledTwice_SecondCallIsANoOp()
    {
        Directory.CreateDirectory(_oldPath);
        File.WriteAllText(Path.Combine(_oldPath, "settings.json"), "{}");
        var service = new AppDataMigrationService(_oldPath, _newPath, NullLogger<AppDataMigrationService>.Instance);

        service.MigrateIfNeeded();
        service.MigrateIfNeeded();

        Assert.True(Directory.Exists(_newPath));
        Assert.False(Directory.Exists(_oldPath));
    }

    [Fact]
    public void LegacyAppDataPath_PointsAtTheOldBridgeFolderNotEmuBridge()
    {
        // The exact case a blind text replace corrupted once already (see the class-level comment
        // on the real property) — asserted directly against the internal property itself, not
        // just inferred from constructor behavior, so a future rename/refactor can't silently
        // reintroduce the same mistake and still pass every other test in this file.
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Bridge");

        Assert.Equal(expected, AppDataMigrationService.LegacyAppDataPath);
    }

    [Fact]
    public void MigrateIfNeeded_PreservesSubfoldersLikeImageCacheAndEmulators()
    {
        Directory.CreateDirectory(Path.Combine(_oldPath, "ImageCache"));
        File.WriteAllText(Path.Combine(_oldPath, "ImageCache", "cover.png"), "fake-image");
        Directory.CreateDirectory(Path.Combine(_oldPath, "Emulators", "retroarch"));
        var service = new AppDataMigrationService(_oldPath, _newPath, NullLogger<AppDataMigrationService>.Instance);

        service.MigrateIfNeeded();

        Assert.True(File.Exists(Path.Combine(_newPath, "ImageCache", "cover.png")));
        Assert.True(Directory.Exists(Path.Combine(_newPath, "Emulators", "retroarch")));
    }

    [Fact]
    public void MigrateIfNeeded_FolderAlreadyMovedButPathsStillStale_RewritesThemAnyway()
    {
        // The exact real-world state a partial/earlier migration can leave behind: the folder
        // move already happened (old folder gone, new one present with emubridge.db inside) but
        // whatever ran it didn't yet know to rewrite stored absolute paths - so they're still
        // pointing at the old, now-nonexistent folder. A naive "new folder exists -> nothing to
        // do" early-out would leave this broken forever, since the move itself never runs again.
        Directory.CreateDirectory(_newPath);
        var staleOldPath = Path.Combine(_oldPath, "ImageCache", "cover.png");
        Guid gameId;
        using (var db = new LiteDatabase(Path.Combine(_newPath, "emubridge.db")))
        {
            gameId = Guid.NewGuid();
            db.GetCollection<BoxArt>("boxArt").Insert(new BoxArt
            {
                Id = Guid.NewGuid(),
                GameId = gameId,
                LocalPath = staleOldPath
            });
        }
        var service = new AppDataMigrationService(_oldPath, _newPath, NullLogger<AppDataMigrationService>.Instance);

        service.MigrateIfNeeded();

        using var db2 = new LiteDatabase(Path.Combine(_newPath, "emubridge.db"));
        var record = db2.GetCollection<BoxArt>("boxArt").FindOne(b => b.GameId == gameId);
        Assert.Equal(Path.Combine(_newPath, "ImageCache", "cover.png"), record.LocalPath);
    }

    // The real bug found via interactive use: moving the folder relocates files, but a LiteDB
    // record's own *stored* absolute-path string (computed before the rename) doesn't update
    // itself just because the folder it pointed at moved. These tests build a real bridge.db with
    // real BoxArt/Emulator records first, so they exercise the actual LiteDB rewrite path, not a
    // simulation of it.
    private Guid SeedBoxArtRecord(string? localPath, string? verticalLocalPath, List<string> screenshotPaths)
    {
        Directory.CreateDirectory(_oldPath);
        using var db = new LiteDatabase(Path.Combine(_oldPath, "bridge.db"));
        var gameId = Guid.NewGuid();
        db.GetCollection<BoxArt>("boxArt").Insert(new BoxArt
        {
            Id = Guid.NewGuid(),
            GameId = gameId,
            LocalPath = localPath,
            VerticalLocalPath = verticalLocalPath,
            ScreenshotLocalPaths = screenshotPaths
        });
        return gameId;
    }

    [Fact]
    public void MigrateIfNeeded_BoxArtLocalPathUnderOldFolder_RewrittenToNewFolder()
    {
        var oldImagePath = Path.Combine(_oldPath, "ImageCache", "cover.png");
        var gameId = SeedBoxArtRecord(oldImagePath, null, []);
        var service = new AppDataMigrationService(_oldPath, _newPath, NullLogger<AppDataMigrationService>.Instance);

        service.MigrateIfNeeded();

        using var migratedDb = new LiteDatabase(Path.Combine(_newPath, "emubridge.db"));
        var record = migratedDb.GetCollection<BoxArt>("boxArt").FindOne(b => b.GameId == gameId);
        Assert.Equal(Path.Combine(_newPath, "ImageCache", "cover.png"), record.LocalPath);
    }

    [Fact]
    public void MigrateIfNeeded_BoxArtVerticalAndScreenshotPaths_AllRewritten()
    {
        var oldVertical = Path.Combine(_oldPath, "ImageCache", "vertical.png");
        var oldShot1 = Path.Combine(_oldPath, "ImageCache", "shot1.png");
        var oldShot2 = Path.Combine(_oldPath, "ImageCache", "shot2.png");
        var gameId = SeedBoxArtRecord(null, oldVertical, [oldShot1, oldShot2]);
        var service = new AppDataMigrationService(_oldPath, _newPath, NullLogger<AppDataMigrationService>.Instance);

        service.MigrateIfNeeded();

        using var migratedDb = new LiteDatabase(Path.Combine(_newPath, "emubridge.db"));
        var record = migratedDb.GetCollection<BoxArt>("boxArt").FindOne(b => b.GameId == gameId);
        Assert.Equal(Path.Combine(_newPath, "ImageCache", "vertical.png"), record.VerticalLocalPath);
        Assert.Equal(
            [Path.Combine(_newPath, "ImageCache", "shot1.png"), Path.Combine(_newPath, "ImageCache", "shot2.png")],
            record.ScreenshotLocalPaths);
    }

    [Fact]
    public void MigrateIfNeeded_BoxArtWithNullPaths_DoesNotThrow()
    {
        var gameId = SeedBoxArtRecord(null, null, []);
        var service = new AppDataMigrationService(_oldPath, _newPath, NullLogger<AppDataMigrationService>.Instance);

        service.MigrateIfNeeded();

        using var migratedDb = new LiteDatabase(Path.Combine(_newPath, "emubridge.db"));
        var record = migratedDb.GetCollection<BoxArt>("boxArt").FindOne(b => b.GameId == gameId);
        Assert.Null(record.LocalPath);
        Assert.Null(record.VerticalLocalPath);
        Assert.Empty(record.ScreenshotLocalPaths);
    }

    [Fact]
    public void MigrateIfNeeded_AutoInstalledEmulatorExecutablePathUnderOldFolder_Rewritten()
    {
        Directory.CreateDirectory(_oldPath);
        var oldExePath = Path.Combine(_oldPath, "Emulators", "retroarch", "RetroArch.exe");
        using (var db = new LiteDatabase(Path.Combine(_oldPath, "bridge.db")))
        {
            db.GetCollection<Emulator>("emulators").Insert(new Emulator
            {
                Id = Guid.NewGuid(),
                Name = "RetroArch",
                ExecutablePath = oldExePath,
                InstallSource = InstallSource.EmuBridgeManaged
            });
        }
        var service = new AppDataMigrationService(_oldPath, _newPath, NullLogger<AppDataMigrationService>.Instance);

        service.MigrateIfNeeded();

        using var migratedDb = new LiteDatabase(Path.Combine(_newPath, "emubridge.db"));
        var record = migratedDb.GetCollection<Emulator>("emulators").FindOne(e => e.Name == "RetroArch");
        Assert.Equal(Path.Combine(_newPath, "Emulators", "retroarch", "RetroArch.exe"), record.ExecutablePath);
    }

    [Fact]
    public void MigrateIfNeeded_UserConfiguredEmulatorOutsideAppData_PathLeftUntouched()
    {
        // A manually-pointed emulator living entirely outside AppData (the common case for a
        // user-supplied install, as opposed to Auto-Install) never had anything to do with the
        // old folder - its path must not be rewritten just because it happens to run after a
        // migration.
        Directory.CreateDirectory(_oldPath);
        var externalExePath = Path.Combine(Path.GetTempPath(), "SomeOtherFolder", "RetroArch.exe");
        using (var db = new LiteDatabase(Path.Combine(_oldPath, "bridge.db")))
        {
            db.GetCollection<Emulator>("emulators").Insert(new Emulator
            {
                Id = Guid.NewGuid(),
                Name = "UserRetroArch",
                ExecutablePath = externalExePath,
                InstallSource = InstallSource.UserProvided
            });
        }
        var service = new AppDataMigrationService(_oldPath, _newPath, NullLogger<AppDataMigrationService>.Instance);

        service.MigrateIfNeeded();

        using var migratedDb = new LiteDatabase(Path.Combine(_newPath, "emubridge.db"));
        var record = migratedDb.GetCollection<Emulator>("emulators").FindOne(e => e.Name == "UserRetroArch");
        Assert.Equal(externalExePath, record.ExecutablePath);
    }

    [Fact]
    public void MigrateIfNeeded_EmulatorWithLegacyBridgeManagedInstallSource_BecomesReadableAfterMigration()
    {
        // The real crash found via interactive use: a pre-existing Emulator row saved
        // InstallSource as the literal string "BridgeManaged" - the enum member itself is now
        // named EmuBridgeManaged, so LiteDB's normal by-name enum deserialization throws
        // ArgumentException the instant anything reads this row through the strongly-typed
        // Emulator model. Inserted as raw BSON here (not via the Emulator/InstallSource enum
        // directly) specifically BECAUSE that member no longer exists to construct - the same
        // constraint a real already-installed user's on-disk data is under.
        Directory.CreateDirectory(_oldPath);
        var exePath = Path.Combine(_oldPath, "Emulators", "retroarch", "RetroArch.exe");
        using (var db = new LiteDatabase(Path.Combine(_oldPath, "bridge.db")))
        {
            var rawEmulators = db.GetCollection("emulators");
            var doc = new BsonDocument
            {
                ["_id"] = Guid.NewGuid(),
                ["Name"] = "RetroArch",
                ["ExecutablePath"] = exePath,
                ["InstallSource"] = "BridgeManaged"
            };
            rawEmulators.Insert(doc);
        }
        var service = new AppDataMigrationService(_oldPath, _newPath, NullLogger<AppDataMigrationService>.Instance);

        // The real bug crashed exactly here, inside MigrateIfNeeded - not on some later, unrelated
        // read - so simply not throwing is itself the primary assertion.
        service.MigrateIfNeeded();

        using var migratedDb = new LiteDatabase(Path.Combine(_newPath, "emubridge.db"));
        var record = migratedDb.GetCollection<Emulator>("emulators").FindOne(e => e.Name == "RetroArch");
        Assert.Equal(InstallSource.EmuBridgeManaged, record.InstallSource);
        Assert.Equal(Path.Combine(_newPath, "Emulators", "retroarch", "RetroArch.exe"), record.ExecutablePath);
    }
}
