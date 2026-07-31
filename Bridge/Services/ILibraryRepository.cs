using Bridge.Models;

namespace Bridge.Services;

public interface ILibraryRepository
{
    Task<IReadOnlyList<Platform>> GetPlatformsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ScanFolder>> GetScanFoldersAsync(CancellationToken ct = default);
    Task AddScanFolderAsync(ScanFolder folder, CancellationToken ct = default);

    Task<IReadOnlyList<Game>> GetAllGamesAsync(CancellationToken ct = default);
    Task UpsertGameAsync(Game game, CancellationToken ct = default);
    Task MarkGamesMissingAsync(IEnumerable<Guid> gameIds, CancellationToken ct = default);

    Task<BoxArt?> GetBoxArtAsync(Guid gameId, CancellationToken ct = default);
    Task UpsertBoxArtAsync(BoxArt boxArt, CancellationToken ct = default);
}
