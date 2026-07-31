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
    }

    private void EnsureIndexes()
    {
        _db.GetCollection<Game>(GamesCollectionName)
            .EnsureIndex(g => g.Path, unique: true);

        _db.GetCollection<BoxArt>(BoxArtCollectionName)
            .EnsureIndex(b => b.GameId, unique: true);
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

        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(Config.SeedSystemsResourceName);
        if (stream is null)
        {
            _logger.LogError(
                "Embedded seed resource {ResourceName} not found; only the unknown-platform sentinel was seeded.",
                Config.SeedSystemsResourceName);
            return;
        }

        try
        {
            var seedPlatforms = System.Text.Json.JsonSerializer.Deserialize<List<Platform>>(stream) ?? [];
            foreach (var platform in seedPlatforms)
            {
                platforms.Insert(platform);
            }

            _logger.LogInformation("Seeded {Count} built-in platforms.", seedPlatforms.Count);
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogError(
                ex,
                "Failed to parse embedded seed resource {ResourceName}; only the unknown-platform sentinel was seeded.",
                Config.SeedSystemsResourceName);
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

    public Task<BoxArt?> GetBoxArtAsync(Guid gameId, CancellationToken ct = default)
    {
        var result = _db.GetCollection<BoxArt>(BoxArtCollectionName)
            .FindOne(b => b.GameId == gameId);
        return Task.FromResult<BoxArt?>(result);
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

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }
}
