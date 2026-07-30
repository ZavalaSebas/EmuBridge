using Bridge.Models;
using Bridge.Services;

namespace Bridge.Tests.Services;

internal class FakeLibraryRepository : ILibraryRepository
{
    public List<Platform> Platforms { get; } = [];
    public List<ScanFolder> ScanFolders { get; } = [];
    public List<Game> Games { get; } = [];

    public Task<IReadOnlyList<Platform>> GetPlatformsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Platform>>(Platforms.ToList());

    public Task<IReadOnlyList<ScanFolder>> GetScanFoldersAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ScanFolder>>(ScanFolders.ToList());

    public Task AddScanFolderAsync(ScanFolder folder, CancellationToken ct = default)
    {
        ScanFolders.Add(folder);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Game>> GetAllGamesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Game>>(Games.ToList());

    public Task UpsertGameAsync(Game game, CancellationToken ct = default)
    {
        var index = Games.FindIndex(g => g.Id == game.Id);
        if (index >= 0)
        {
            Games[index] = game;
        }
        else
        {
            Games.Add(game);
        }

        return Task.CompletedTask;
    }

    public Task MarkGamesMissingAsync(IEnumerable<Guid> gameIds, CancellationToken ct = default)
    {
        foreach (var id in gameIds)
        {
            var game = Games.FirstOrDefault(g => g.Id == id);
            if (game is not null)
            {
                game.IsMissing = true;
            }
        }

        return Task.CompletedTask;
    }
}
