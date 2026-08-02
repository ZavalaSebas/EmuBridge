using System.IO;
using System.Reflection;
using Bridge.Models;
using LiteDB;
using Microsoft.Extensions.Logging;

namespace Bridge.Services;

public class LibraryRepository : ILibraryRepository, IDisposable
{
    private const string PlatformsCollectionName = "platforms";
    private const string GamesCollectionName = "games";
    private const string ScanFoldersCollectionName = "scanFolders";
    private const string BoxArtCollectionName = "boxArt";
    private const string EmulatorsCollectionName = "emulators";
    private const string EmulatorProfilesCollectionName = "emulatorProfiles";

    // Legacy collection name from the pre-ADR-11 1:1 EmulatorConfig shape. Only ever read once,
    // during MigrateLegacyEmulatorConfigsIfNeeded(), then dropped.
    private const string LegacyEmulatorConfigsCollectionName = "emulatorConfigs";

    private readonly LiteDatabase _db;
    private readonly ILogger<LibraryRepository> _logger;

    public LibraryRepository(ILogger<LibraryRepository> logger)
        : this(Config.LibraryDbPath, logger)
    {
    }

    public LibraryRepository(string dbPath, ILogger<LibraryRepository> logger)
    {
        _logger = logger;

        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _db = new LiteDatabase(dbPath);
        EnsureIndexes();
        SeedPlatformsIfEmpty();
        ReconcileSeedPlatformExtensions();
        MigrateLegacyEmulatorConfigsIfNeeded();
    }

    private void EnsureIndexes()
    {
        _db.GetCollection<Game>(GamesCollectionName)
            .EnsureIndex(g => g.Path, unique: true);

        _db.GetCollection<BoxArt>(BoxArtCollectionName)
            .EnsureIndex(b => b.GameId, unique: true);

        // Dedup key for "same physical install reused across platforms" (ADR-11). Defense in
        // depth backing EmulatorService's find-by-path-then-upsert logic — mirrors the existing
        // Game.Path / BoxArt.GameId unique-index pattern in this repository.
        _db.GetCollection<Emulator>(EmulatorsCollectionName)
            .EnsureIndex(e => e.ExecutablePath, unique: true);

        // No unique index on EmulatorProfile.(PlatformId, GameId) — enforcement happens in
        // UpsertEmulatorProfileAsync's own find-then-replace below, not the schema. Originally
        // documented (ADR-11) as "PlatformId alone, so a future per-game UI doesn't need another
        // migration" — that was aspirational, not what the code actually did until ADR-24 added
        // GameId and the composite-key lookup for real.
    }

    private void SeedPlatformsIfEmpty()
    {
        var platforms = _db.GetCollection<Platform>(PlatformsCollectionName);
        if (platforms.Count() > 0)
        {
            return;
        }

        platforms.Insert(new Platform
        {
            Id = Config.UnknownPlatformId,
            Name = Config.UnknownPlatformName,
            Extensions = []
        });

        var seedPlatforms = LoadSeedPlatforms();
        if (seedPlatforms is null)
        {
            return;
        }

        foreach (var platform in seedPlatforms)
        {
            platforms.Insert(platform);
        }

        _logger.LogInformation("Seeded {Count} built-in platforms.", seedPlatforms.Count);
    }

    // SeedPlatformsIfEmpty only ever runs once per database, ever — gated on the whole Platform
    // collection being empty, which stops being true after the very first open. Without this,
    // editing SeedSystems.json (a new extension on an existing platform, or a whole new platform)
    // would only ever reach brand-new databases; every already-seeded bridge.db — including every
    // existing user's — would keep the old data forever. Runs on every open, not gated, since the
    // cost is trivial (15 small list comparisons). Reconciles by union, never removes an
    // extension — a platform row can carry more than the seed without this silently deleting
    // anything (e.g. a future manual/custom addition). Deliberately does not touch Name — only
    // Extensions was ever the problem, and syncing Name would risk overwriting something a user
    // has already seen/relied on for a reason not in scope here.
    private void ReconcileSeedPlatformExtensions()
    {
        var seedPlatforms = LoadSeedPlatforms();
        if (seedPlatforms is null)
        {
            return;
        }

        var platforms = _db.GetCollection<Platform>(PlatformsCollectionName);
        foreach (var seedPlatform in seedPlatforms)
        {
            var existing = platforms.FindById(seedPlatform.Id);
            if (existing is null)
            {
                // A platform added to SeedSystems.json after this database was first seeded —
                // same one-shot-seeding gap as a missing extension, same fix.
                platforms.Insert(seedPlatform);
                _logger.LogInformation("Added new seed platform {PlatformId} to an already-seeded database.", seedPlatform.Id);
                continue;
            }

            var merged = existing.Extensions
                .Union(seedPlatform.Extensions, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (merged.Count != existing.Extensions.Count)
            {
                _logger.LogInformation(
                    "Updated {PlatformId}'s recognized extensions from an updated seed definition: [{Old}] -> [{New}].",
                    seedPlatform.Id,
                    string.Join(", ", existing.Extensions),
                    string.Join(", ", merged));

                existing.Extensions = merged;
                platforms.Update(existing);
            }
        }
    }

    private List<Platform>? LoadSeedPlatforms()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(Config.SeedSystemsResourceName);
        if (stream is null)
        {
            _logger.LogError("Embedded seed resource {ResourceName} not found.", Config.SeedSystemsResourceName);
            return null;
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<Platform>>(stream) ?? [];
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse embedded seed resource {ResourceName}.", Config.SeedSystemsResourceName);
            return null;
        }
    }

    public Task<IReadOnlyList<Platform>> GetPlatformsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<Platform> result = _db.GetCollection<Platform>(PlatformsCollectionName)
            .FindAll()
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<ScanFolder>> GetScanFoldersAsync(CancellationToken ct = default)
    {
        IReadOnlyList<ScanFolder> result = _db.GetCollection<ScanFolder>(ScanFoldersCollectionName)
            .FindAll()
            .ToList();
        return Task.FromResult(result);
    }

    public Task AddScanFolderAsync(ScanFolder folder, CancellationToken ct = default)
    {
        _db.GetCollection<ScanFolder>(ScanFoldersCollectionName).Insert(folder);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Game>> GetAllGamesAsync(CancellationToken ct = default)
    {
        IReadOnlyList<Game> result = _db.GetCollection<Game>(GamesCollectionName)
            .FindAll()
            .ToList();
        return Task.FromResult(result);
    }

    public Task UpsertGameAsync(Game game, CancellationToken ct = default)
    {
        _db.GetCollection<Game>(GamesCollectionName).Upsert(game);
        return Task.CompletedTask;
    }

    public Task MarkGamesMissingAsync(IEnumerable<Guid> gameIds, CancellationToken ct = default)
    {
        var games = _db.GetCollection<Game>(GamesCollectionName);
        foreach (var id in gameIds)
        {
            var game = games.FindById(id);
            if (game is null)
            {
                continue;
            }

            game.IsMissing = true;
            games.Update(game);
        }

        return Task.CompletedTask;
    }

    public Task DeleteGameAsync(Guid gameId, CancellationToken ct = default)
    {
        _db.GetCollection<Game>(GamesCollectionName).Delete(gameId);
        return Task.CompletedTask;
    }

    public Task<BoxArt?> GetBoxArtAsync(Guid gameId, CancellationToken ct = default)
    {
        var result = _db.GetCollection<BoxArt>(BoxArtCollectionName)
            .FindOne(b => b.GameId == gameId);
        return Task.FromResult<BoxArt?>(result);
    }

    public Task<IReadOnlyList<BoxArt>> GetAllBoxArtAsync(CancellationToken ct = default)
    {
        IReadOnlyList<BoxArt> result = _db.GetCollection<BoxArt>(BoxArtCollectionName)
            .FindAll()
            .ToList();
        return Task.FromResult(result);
    }

    public Task UpsertBoxArtAsync(BoxArt boxArt, CancellationToken ct = default)
    {
        var collection = _db.GetCollection<BoxArt>(BoxArtCollectionName);
        var existing = collection.FindOne(b => b.GameId == boxArt.GameId);
        if (existing is not null)
        {
            boxArt.Id = existing.Id;
        }
        else if (boxArt.Id == Guid.Empty)
        {
            boxArt.Id = Guid.NewGuid();
        }

        collection.Upsert(boxArt);
        return Task.CompletedTask;
    }

    public Task DeleteBoxArtAsync(Guid gameId, CancellationToken ct = default)
    {
        var collection = _db.GetCollection<BoxArt>(BoxArtCollectionName);
        var existing = collection.FindOne(b => b.GameId == gameId);
        if (existing is not null)
        {
            collection.Delete(existing.Id);
        }

        return Task.CompletedTask;
    }

    public Task<Emulator?> GetEmulatorByIdAsync(Guid id, CancellationToken ct = default)
    {
        var result = _db.GetCollection<Emulator>(EmulatorsCollectionName).FindById(id);
        return Task.FromResult<Emulator?>(result);
    }

    public Task<Emulator?> GetEmulatorByExecutablePathAsync(string executablePath, CancellationToken ct = default)
    {
        var result = _db.GetCollection<Emulator>(EmulatorsCollectionName)
            .FindOne(e => e.ExecutablePath.Equals(executablePath, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult<Emulator?>(result);
    }

    public Task<Emulator?> GetEmulatorByKnownEmulatorIdAsync(string knownEmulatorId, CancellationToken ct = default)
    {
        var result = _db.GetCollection<Emulator>(EmulatorsCollectionName)
            .FindOne(e => e.KnownEmulatorId == knownEmulatorId);
        return Task.FromResult<Emulator?>(result);
    }

    public Task<Emulator> UpsertEmulatorAsync(Emulator emulator, CancellationToken ct = default)
    {
        var collection = _db.GetCollection<Emulator>(EmulatorsCollectionName);
        var existing = collection.FindOne(e => e.ExecutablePath.Equals(emulator.ExecutablePath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            emulator.Id = existing.Id;
        }
        else if (emulator.Id == Guid.Empty)
        {
            emulator.Id = Guid.NewGuid();
        }

        collection.Upsert(emulator);
        return Task.FromResult(emulator);
    }

    // GameId == null specifically — the platform-wide default, not any per-game override that
    // might also exist for this platform (ADR-24). Every existing caller of this method wants the
    // default; game-specific lookup goes through GetEmulatorProfileForGameAsync instead.
    public Task<EmulatorProfile?> GetEmulatorProfileByPlatformIdAsync(string platformId, CancellationToken ct = default)
    {
        var result = _db.GetCollection<EmulatorProfile>(EmulatorProfilesCollectionName)
            .FindOne(p => p.PlatformId == platformId && p.GameId == null);
        return Task.FromResult<EmulatorProfile?>(result);
    }

    // GameId is unique by itself (a Game has exactly one PlatformId, fixed at scan time), so no
    // need to also match PlatformId here — see ARCHITECTURE.md -> ADR-24.
    public Task<EmulatorProfile?> GetEmulatorProfileForGameAsync(Guid gameId, CancellationToken ct = default)
    {
        var result = _db.GetCollection<EmulatorProfile>(EmulatorProfilesCollectionName)
            .FindOne(p => p.GameId == gameId);
        return Task.FromResult<EmulatorProfile?>(result);
    }

    public Task UpsertEmulatorProfileAsync(EmulatorProfile profile, CancellationToken ct = default)
    {
        var collection = _db.GetCollection<EmulatorProfile>(EmulatorProfilesCollectionName);
        var existing = collection.FindOne(p => p.PlatformId == profile.PlatformId && p.GameId == profile.GameId);
        if (existing is not null)
        {
            profile.Id = existing.Id;
        }
        else if (profile.Id == Guid.Empty)
        {
            profile.Id = Guid.NewGuid();
        }

        collection.Upsert(profile);
        return Task.CompletedTask;
    }

    // Direct delete, no "still referenced elsewhere" check — unlike BoxArt's cached image files
    // (deduped by URL hash, so two rows can share one file), a per-game EmulatorProfile row is
    // looked up only by its own GameId and nothing else in the codebase stores a reference to
    // EmulatorProfile.Id, so it can never be shared between games (ARCHITECTURE.md -> ADR-24).
    // No-op if the game has no override — same silent-skip idiom as DeleteGameAsync/DeleteBoxArtAsync.
    public Task DeleteEmulatorProfileForGameAsync(Guid gameId, CancellationToken ct = default)
    {
        _db.GetCollection<EmulatorProfile>(EmulatorProfilesCollectionName)
            .DeleteMany(p => p.GameId == gameId);
        return Task.CompletedTask;
    }

    // One-time migration off the pre-ADR-11 1:1 EmulatorConfig shape. Only runs if the new
    // collections are both still empty and the legacy collection actually has data — safe to
    // call unconditionally on every startup. Dedupes by ExecutablePath exactly like
    // UpsertEmulatorAsync does going forward, so two legacy rows that happened to point at the
    // same .exe collapse into one Emulator with two EmulatorProfile rows, not two Emulators.
    private void MigrateLegacyEmulatorConfigsIfNeeded()
    {
        var emulators = _db.GetCollection<Emulator>(EmulatorsCollectionName);
        var profiles = _db.GetCollection<EmulatorProfile>(EmulatorProfilesCollectionName);
        if (emulators.Count() > 0 || profiles.Count() > 0 || !_db.CollectionExists(LegacyEmulatorConfigsCollectionName))
        {
            return;
        }

        var legacyConfigs = _db.GetCollection<LegacyEmulatorConfig>(LegacyEmulatorConfigsCollectionName).FindAll().ToList();
        if (legacyConfigs.Count == 0)
        {
            return;
        }

        var emulatorIdByPath = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var legacy in legacyConfigs)
        {
            if (!emulatorIdByPath.TryGetValue(legacy.ExecutablePath, out var emulatorId))
            {
                emulatorId = Guid.NewGuid();
                emulators.Insert(new Emulator
                {
                    Id = emulatorId,
                    KnownEmulatorId = null,
                    Name = legacy.Name,
                    ExecutablePath = legacy.ExecutablePath,
                    InstallSource = InstallSource.UserProvided,
                    InstalledSha256 = null
                });
                emulatorIdByPath[legacy.ExecutablePath] = emulatorId;
            }

            profiles.Insert(new EmulatorProfile
            {
                Id = Guid.NewGuid(),
                EmulatorId = emulatorId,
                PlatformId = legacy.PlatformId,
                ArgumentTemplate = legacy.ArgumentTemplate
            });
        }

        _db.DropCollection(LegacyEmulatorConfigsCollectionName);
        _logger.LogInformation(
            "Migrated {ConfigCount} legacy EmulatorConfig row(s) into {EmulatorCount} Emulator(s) and {ProfileCount} EmulatorProfile(s).",
            legacyConfigs.Count,
            emulatorIdByPath.Count,
            legacyConfigs.Count);
    }

    // Shape-only mirror of the deleted Models/EmulatorConfig.cs, scoped to this one migration —
    // not exposed outside LibraryRepository.
    private class LegacyEmulatorConfig
    {
        public Guid Id { get; set; }
        public string PlatformId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public string ArgumentTemplate { get; set; } = string.Empty;
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }
}
