using System.IO;
using Bridge.Models;
using Microsoft.Extensions.Logging;

namespace Bridge.Services;

public class RomScannerService : IRomScannerService
{
    private readonly ILibraryRepository _repository;
    private readonly ILogger<RomScannerService> _logger;

    public RomScannerService(ILibraryRepository repository, ILogger<RomScannerService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<ScanResult> ScanAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var result = new ScanResult();

        var scanFolders = await _repository.GetScanFoldersAsync(ct);
        var platforms = await _repository.GetPlatformsAsync(ct);
        var existingGames = await _repository.GetAllGamesAsync(ct);

        var extensionToPlatformId = BuildExtensionMap(platforms);

        var existingGamesById = existingGames.ToDictionary(g => g.Id);
        var gamesByPath = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase);
        var unseenGameIds = new HashSet<Guid>();
        foreach (var game in existingGames)
        {
            gamesByPath[game.Path] = game;
            unseenGameIds.Add(game.Id);
        }

        var filesProcessed = 0;

        foreach (var folder in scanFolders)
        {
            ct.ThrowIfCancellationRequested();

            if (!Directory.Exists(folder.Path))
            {
                _logger.LogWarning("Configured scan folder {Folder} does not exist; skipping.", folder.Path);
                result.SkippedFolders.Add(new SkippedFolder(folder.Path, "Folder not found"));
                continue;
            }

            foreach (var filePath in EnumerateFilesSafely(folder.Path, result))
            {
                ct.ThrowIfCancellationRequested();

                await ProcessFileAsync(filePath, extensionToPlatformId, gamesByPath, unseenGameIds, result, ct);

                filesProcessed++;
                progress?.Report(filesProcessed);
            }
        }

        var newlyMissingIds = unseenGameIds
            .Where(id => !existingGamesById[id].IsMissing)
            .ToList();

        if (newlyMissingIds.Count > 0)
        {
            await _repository.MarkGamesMissingAsync(newlyMissingIds, ct);
            result.GamesMarkedMissing = newlyMissingIds.Count;
        }

        return result;
    }

    private async Task ProcessFileAsync(
        string filePath,
        IReadOnlyDictionary<string, string> extensionToPlatformId,
        Dictionary<string, Game> gamesByPath,
        HashSet<Guid> unseenGameIds,
        ScanResult result,
        CancellationToken ct)
    {
        long fileSize;
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
            {
                _logger.LogWarning("File {File} disappeared before it could be processed; skipping.", filePath);
                result.SkippedFiles.Add(new SkippedFile(filePath, "File no longer exists"));
                return;
            }

            fileSize = fileInfo.Length;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _logger.LogWarning(ex, "Could not access file {File}; skipping.", filePath);
            result.SkippedFiles.Add(new SkippedFile(filePath, ex.Message));
            return;
        }

        if (fileSize == 0)
        {
            _logger.LogWarning("File {File} is empty; skipping.", filePath);
            result.SkippedFiles.Add(new SkippedFile(filePath, "Empty file"));
            return;
        }

        var extension = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        var platformId = extensionToPlatformId.GetValueOrDefault(extension, Config.UnknownPlatformId);

        if (gamesByPath.TryGetValue(filePath, out var existingGame))
        {
            unseenGameIds.Remove(existingGame.Id);

            var changed = existingGame.PlatformId != platformId || existingGame.IsMissing;
            existingGame.PlatformId = platformId;
            existingGame.IsMissing = false;

            if (changed)
            {
                await _repository.UpsertGameAsync(existingGame, ct);
                result.GamesUpdated++;
            }

            return;
        }

        var newGame = new Game
        {
            Id = Guid.NewGuid(),
            Path = filePath,
            Name = Path.GetFileNameWithoutExtension(filePath),
            PlatformId = platformId,
            IsMissing = false
        };

        await _repository.UpsertGameAsync(newGame, ct);
        gamesByPath[filePath] = newGame;
        result.GamesAdded++;
    }

    private IEnumerable<string> EnumerateFilesSafely(string rootPath, ScanResult result)
    {
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(rootPath);

        while (pendingDirectories.Count > 0)
        {
            var currentDirectory = pendingDirectories.Pop();
            string[] files;
            string[] subDirectories;

            try
            {
                files = Directory.GetFiles(currentDirectory);
                subDirectories = Directory.GetDirectories(currentDirectory);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
            {
                _logger.LogWarning(ex, "Skipping inaccessible directory {Directory}.", currentDirectory);
                result.SkippedFolders.Add(new SkippedFolder(currentDirectory, ex.Message));
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            foreach (var subDirectory in subDirectories)
            {
                pendingDirectories.Push(subDirectory);
            }
        }
    }

    private static Dictionary<string, string> BuildExtensionMap(IReadOnlyList<Platform> platforms)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var platform in platforms)
        {
            foreach (var extension in platform.Extensions)
            {
                map[extension] = platform.Id;
            }
        }

        return map;
    }
}
