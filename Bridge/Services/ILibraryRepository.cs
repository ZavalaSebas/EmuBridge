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
    Task<IReadOnlyList<BoxArt>> GetAllBoxArtAsync(CancellationToken ct = default);
    Task UpsertBoxArtAsync(BoxArt boxArt, CancellationToken ct = default);

    Task<Emulator?> GetEmulatorByIdAsync(Guid id, CancellationToken ct = default);

    // Dedup key for "reuse the same physical install across platforms" (e.g. one RetroArch
    // instance backing many EmulatorProfile rows). Case-insensitive — Windows paths.
    Task<Emulator?> GetEmulatorByExecutablePathAsync(string executablePath, CancellationToken ct = default);

    // Resolves emulator.Id (existing row for this ExecutablePath, or a new Guid) and returns the
    // stored emulator so the caller can build an EmulatorProfile against the right EmulatorId
    // without a separate round trip.
    Task<Emulator> UpsertEmulatorAsync(Emulator emulator, CancellationToken ct = default);

    Task<EmulatorProfile?> GetEmulatorProfileByPlatformIdAsync(string platformId, CancellationToken ct = default);
    Task UpsertEmulatorProfileAsync(EmulatorProfile profile, CancellationToken ct = default);
}
