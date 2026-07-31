using Bridge.Models;
using Bridge.Services;

namespace Bridge.Tests.Services;

internal class FakeLibraryRepository : ILibraryRepository
{
    public List<Platform> Platforms { get; } = [];
    public List<ScanFolder> ScanFolders { get; } = [];
    public List<Game> Games { get; } = [];
    public List<BoxArt> BoxArtRecords { get; } = [];
    public List<Emulator> Emulators { get; } = [];
    public List<EmulatorProfile> EmulatorProfiles { get; } = [];

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

    public Task<BoxArt?> GetBoxArtAsync(Guid gameId, CancellationToken ct = default)
        => Task.FromResult(BoxArtRecords.FirstOrDefault(b => b.GameId == gameId));

    public Task<IReadOnlyList<BoxArt>> GetAllBoxArtAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<BoxArt>>(BoxArtRecords.ToList());

    public Task UpsertBoxArtAsync(BoxArt boxArt, CancellationToken ct = default)
    {
        var index = BoxArtRecords.FindIndex(b => b.GameId == boxArt.GameId);
        if (index >= 0)
        {
            boxArt.Id = BoxArtRecords[index].Id;
            BoxArtRecords[index] = boxArt;
        }
        else
        {
            if (boxArt.Id == Guid.Empty)
            {
                boxArt.Id = Guid.NewGuid();
            }

            BoxArtRecords.Add(boxArt);
        }

        return Task.CompletedTask;
    }

    public Task<Emulator?> GetEmulatorByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(Emulators.FirstOrDefault(e => e.Id == id));

    public Task<Emulator?> GetEmulatorByExecutablePathAsync(string executablePath, CancellationToken ct = default)
        => Task.FromResult(Emulators.FirstOrDefault(e => e.ExecutablePath.Equals(executablePath, StringComparison.OrdinalIgnoreCase)));

    public Task<Emulator> UpsertEmulatorAsync(Emulator emulator, CancellationToken ct = default)
    {
        var index = Emulators.FindIndex(e => e.ExecutablePath.Equals(emulator.ExecutablePath, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            emulator.Id = Emulators[index].Id;
            Emulators[index] = emulator;
        }
        else
        {
            if (emulator.Id == Guid.Empty)
            {
                emulator.Id = Guid.NewGuid();
            }

            Emulators.Add(emulator);
        }

        return Task.FromResult(emulator);
    }

    public Task<EmulatorProfile?> GetEmulatorProfileByPlatformIdAsync(string platformId, CancellationToken ct = default)
        => Task.FromResult(EmulatorProfiles.FirstOrDefault(p => p.PlatformId == platformId));

    public Task UpsertEmulatorProfileAsync(EmulatorProfile profile, CancellationToken ct = default)
    {
        var index = EmulatorProfiles.FindIndex(p => p.PlatformId == profile.PlatformId);
        if (index >= 0)
        {
            profile.Id = EmulatorProfiles[index].Id;
            EmulatorProfiles[index] = profile;
        }
        else
        {
            if (profile.Id == Guid.Empty)
            {
                profile.Id = Guid.NewGuid();
            }

            EmulatorProfiles.Add(profile);
        }

        return Task.CompletedTask;
    }
}
