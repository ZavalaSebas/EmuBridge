using System.IO;
using Bridge.Models;
using Bridge.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bridge.Tests.Services;

public class RomScannerServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly FakeLibraryRepository _repository;
    private readonly RomScannerService _scanner;

    public RomScannerServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bridge_scan_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempRoot);

        _repository = new FakeLibraryRepository();
        _repository.Platforms.Add(new Platform { Id = "nes", Name = "NES", Extensions = ["nes"] });
        _repository.Platforms.Add(new Platform { Id = Config.UnknownPlatformId, Name = "Unknown", Extensions = [] });

        _scanner = new RomScannerService(_repository, NullLogger<RomScannerService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private string CreateFile(string relativePath, byte[]? content = null)
    {
        var fullPath = Path.Combine(_tempRoot, relativePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(fullPath, content ?? [1, 2, 3]);
        return fullPath;
    }

    [Fact]
    public async Task ScanAsync_KnownExtension_AssignsCorrectPlatform()
    {
        CreateFile("mario.nes");
        _repository.ScanFolders.Add(new ScanFolder { Id = Guid.NewGuid(), Path = _tempRoot });

        var result = await _scanner.ScanAsync();

        var game = Assert.Single(_repository.Games);
        Assert.Equal("nes", game.PlatformId);
        Assert.Equal(1, result.GamesAdded);
    }

    [Fact]
    public async Task ScanAsync_UnrecognizedExtension_FallsBackToUnknownPlatform()
    {
        CreateFile("mystery.xyz");
        _repository.ScanFolders.Add(new ScanFolder { Id = Guid.NewGuid(), Path = _tempRoot });

        await _scanner.ScanAsync();

        var game = Assert.Single(_repository.Games);
        Assert.Equal(Config.UnknownPlatformId, game.PlatformId);
    }

    [Fact]
    public async Task ScanAsync_SameFileScannedTwice_DoesNotDuplicate()
    {
        CreateFile("mario.nes");
        _repository.ScanFolders.Add(new ScanFolder { Id = Guid.NewGuid(), Path = _tempRoot });

        await _scanner.ScanAsync();
        await _scanner.ScanAsync();

        Assert.Single(_repository.Games);
    }

    [Fact]
    public async Task ScanAsync_FileRemovedSinceLastScan_MarksGameMissing()
    {
        var filePath = CreateFile("mario.nes");
        _repository.ScanFolders.Add(new ScanFolder { Id = Guid.NewGuid(), Path = _tempRoot });
        await _scanner.ScanAsync();

        File.Delete(filePath);
        var result = await _scanner.ScanAsync();

        Assert.True(_repository.Games.Single().IsMissing);
        Assert.Equal(1, result.GamesMarkedMissing);
    }

    [Fact]
    public async Task ScanAsync_FileReappearsAfterBeingMissing_ClearsMissingFlag()
    {
        var filePath = CreateFile("mario.nes");
        _repository.ScanFolders.Add(new ScanFolder { Id = Guid.NewGuid(), Path = _tempRoot });
        await _scanner.ScanAsync();

        File.Delete(filePath);
        await _scanner.ScanAsync();

        CreateFile("mario.nes");
        await _scanner.ScanAsync();

        Assert.False(_repository.Games.Single().IsMissing);
    }

    [Fact]
    public async Task ScanAsync_EmptyFile_IsSkippedAndNotPersisted()
    {
        CreateFile("empty.nes", content: []);
        _repository.ScanFolders.Add(new ScanFolder { Id = Guid.NewGuid(), Path = _tempRoot });

        var result = await _scanner.ScanAsync();

        Assert.Empty(_repository.Games);
        Assert.Single(result.SkippedFiles);
    }

    [Fact]
    public async Task ScanAsync_ConfiguredFolderDoesNotExist_SkipsItAndContinuesOthers()
    {
        var missingFolder = Path.Combine(_tempRoot, "does-not-exist");
        var goodFolder = Path.Combine(_tempRoot, "good");
        Directory.CreateDirectory(goodFolder);
        File.WriteAllBytes(Path.Combine(goodFolder, "mario.nes"), [1, 2, 3]);

        _repository.ScanFolders.Add(new ScanFolder { Id = Guid.NewGuid(), Path = missingFolder });
        _repository.ScanFolders.Add(new ScanFolder { Id = Guid.NewGuid(), Path = goodFolder });

        var result = await _scanner.ScanAsync();

        Assert.Single(result.SkippedFolders);
        Assert.Single(_repository.Games);
    }
}
