using System.IO;
using System.IO.Compression;
using Bridge.Models;
using Bridge.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bridge.Tests.Services;

public class EmulatorInstallerServiceTests : IDisposable
{
    private const string FrontendUrl = "https://example.com/testfrontend.zip";
    private const string CoreUrl = "https://example.com/testcore.zip";

    private readonly string _tempRoot;
    private readonly string _installDirectory;
    private readonly string _frontendArchivePath;
    private readonly string _coreArchivePath;
    private readonly FakeDownloadVerificationService _downloadService;
    private readonly FakeEmulatorService _emulatorService;

    public EmulatorInstallerServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"bridge_installer_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempRoot);
        _installDirectory = Path.Combine(_tempRoot, "Emulators");

        // Real, small, hand-built .zip fixtures — genuinely exercises SharpCompress extraction,
        // not a mock of it. DownloadVerificationService's own hash/size verification is already
        // covered elsewhere, so FakeDownloadVerificationService bypasses it here and just points
        // at these real archives.
        //
        // The frontend entry is deliberately nested (RetroArch-Win64/retroarch.exe), matching the
        // real RetroArch 1.22.2 archive's actual layout — confirmed by extracting the real
        // downloaded .7z after a real "Auto-Install" click failed with
        // ExecutableNotFoundAfterExtraction on 2026-08-03 (see ARCHITECTURE.md -> ADR-11 update).
        // The original fixture used a flat "retroarch.exe" entry, matching the wrong
        // third-party-sourced ExecutableRelativePath the manifest shipped with at the time — every
        // test here passed against that wrong assumption because the fixture matched it, not
        // reality. Keep this nested; flattening it back would silently re-hide the same bug class.
        _frontendArchivePath = Path.Combine(_tempRoot, "frontend.zip");
        BuildZip(_frontendArchivePath, ("RetroArch-Win64/retroarch.exe", [1, 2, 3]));

        _coreArchivePath = Path.Combine(_tempRoot, "core.zip");
        BuildZip(_coreArchivePath, ("fceumm_libretro.dll", [4, 5, 6]));

        _downloadService = new FakeDownloadVerificationService();
        _emulatorService = new FakeEmulatorService();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private static void BuildZip(string path, params (string Name, byte[] Content)[] entries)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name);
            using var stream = entry.Open();
            stream.Write(content);
        }
    }

    private static KnownEmulator MakeCatalogEntry(string platformId = "nes") => new()
    {
        Id = "retroarch",
        Name = "RetroArch",
        Version = "1.22.2",
        DownloadUrl = FrontendUrl,
        Sha256 = "irrelevant-not-checked-by-fake",
        ExpectedSizeBytes = 3,
        ExecutableRelativePath = "RetroArch-Win64\\retroarch.exe",
        Cores =
        [
            new KnownEmulatorCore
            {
                Id = "fceumm",
                PlatformId = platformId,
                DownloadUrl = CoreUrl,
                Sha256 = "irrelevant-not-checked-by-fake",
                ExpectedSizeBytes = 3,
                CoreFileName = "fceumm_libretro.dll",
                CapturedAt = "2026-07-31"
            }
        ]
    };

    private EmulatorInstallerService CreateService(IReadOnlyList<KnownEmulator> catalog)
        => new(_downloadService, _emulatorService, _installDirectory, catalog, NullLogger<EmulatorInstallerService>.Instance);

    [Fact]
    public async Task HasKnownInstallOptionAsync_MatchingVerifiedEntry_ReturnsTrue()
    {
        var service = CreateService([MakeCatalogEntry()]);

        Assert.True(await service.HasKnownInstallOptionAsync("nes"));
    }

    [Fact]
    public async Task HasKnownInstallOptionAsync_NoMatchingPlatform_ReturnsFalse()
    {
        var service = CreateService([MakeCatalogEntry()]);

        Assert.False(await service.HasKnownInstallOptionAsync("snes"));
    }

    [Fact]
    public async Task HasKnownInstallOptionAsync_PlaceholderData_ReturnsFalse()
    {
        var entry = MakeCatalogEntry();
        entry.Sha256 = Config.UnverifiedManifestPlaceholder;
        var service = CreateService([entry]);

        Assert.False(await service.HasKnownInstallOptionAsync("nes"));
    }

    [Fact]
    public async Task InstallAsync_NoKnownCoreForPlatform_ReturnsNoKnownCoreForPlatform()
    {
        var service = CreateService([]);

        var result = await service.InstallAsync("nes");

        Assert.Equal(InstallOutcome.NoKnownCoreForPlatform, result.Outcome);
    }

    [Fact]
    public async Task InstallAsync_UnverifiedManifestData_RefusesToInstallWithoutDownloading()
    {
        var entry = MakeCatalogEntry();
        entry.Cores[0].DownloadUrl = Config.UnverifiedManifestPlaceholder;
        var service = CreateService([entry]);

        var result = await service.InstallAsync("nes");

        Assert.Equal(InstallOutcome.UnverifiedManifestData, result.Outcome);
        Assert.Empty(_downloadService.RequestedUrls);
    }

    [Fact]
    public async Task InstallAsync_FullSuccess_ExtractsFrontendAndCore_RegistersProfile()
    {
        _downloadService.ResultsByUrl[FrontendUrl] = new DownloadResult { Outcome = DownloadOutcome.Success, FilePath = _frontendArchivePath };
        _downloadService.ResultsByUrl[CoreUrl] = new DownloadResult { Outcome = DownloadOutcome.Success, FilePath = _coreArchivePath };
        var service = CreateService([MakeCatalogEntry()]);

        var result = await service.InstallAsync("nes");

        Assert.Equal(InstallOutcome.Success, result.Outcome);

        var emulator = Assert.Single(_emulatorService.InstalledEmulators);
        Assert.Equal("retroarch", emulator.KnownEmulatorId);
        Assert.True(File.Exists(emulator.ExecutablePath));

        var profile = await _emulatorService.GetProfileForPlatformAsync("nes");
        Assert.NotNull(profile);
        Assert.NotNull(profile.CorePath);
        Assert.True(File.Exists(profile.CorePath));
        Assert.Contains("cores", profile.CorePath);
        Assert.Contains("{CorePath}", profile.ArgumentTemplate);
        Assert.Contains("{RomPath}", profile.ArgumentTemplate);
    }

    [Fact]
    public async Task InstallAsync_FrontendDownloadFails_ReturnsDownloadFailedWithPassthroughMessage()
    {
        _downloadService.ResultsByUrl[FrontendUrl] = new DownloadResult { Outcome = DownloadOutcome.HashMismatch, ErrorMessage = "specific message from DownloadVerificationService" };
        var service = CreateService([MakeCatalogEntry()]);

        var result = await service.InstallAsync("nes");

        Assert.Equal(InstallOutcome.DownloadFailed, result.Outcome);
        Assert.Equal("specific message from DownloadVerificationService", result.ErrorMessage);
        Assert.Empty(_emulatorService.InstalledEmulators);
    }

    [Fact]
    public async Task InstallAsync_FrontendExtractionFails_CleansUpPartialDirectory()
    {
        var notAnArchive = Path.Combine(_tempRoot, "not-an-archive.zip");
        File.WriteAllBytes(notAnArchive, [1, 2, 3, 4]);
        _downloadService.ResultsByUrl[FrontendUrl] = new DownloadResult { Outcome = DownloadOutcome.Success, FilePath = notAnArchive };
        var service = CreateService([MakeCatalogEntry()]);

        var result = await service.InstallAsync("nes");

        Assert.Equal(InstallOutcome.ExtractionFailed, result.Outcome);
        Assert.False(Directory.Exists(Path.Combine(_installDirectory, "retroarch")));
        Assert.Empty(_emulatorService.InstalledEmulators);
    }

    [Fact]
    public async Task InstallAsync_ExecutableNotFoundAfterExtraction_ReturnsSpecificOutcome()
    {
        var entry = MakeCatalogEntry();
        entry.ExecutableRelativePath = "wrong-name.exe";
        _downloadService.ResultsByUrl[FrontendUrl] = new DownloadResult { Outcome = DownloadOutcome.Success, FilePath = _frontendArchivePath };
        var service = CreateService([entry]);

        var result = await service.InstallAsync("nes");

        Assert.Equal(InstallOutcome.ExecutableNotFoundAfterExtraction, result.Outcome);
        Assert.False(Directory.Exists(Path.Combine(_installDirectory, "retroarch")));
    }

    // The actual reason ADR-14's two-level failure handling exists: a working frontend install
    // is a valid, reusable state even if this specific core's download failed.
    [Fact]
    public async Task InstallAsync_CoreDownloadFails_DoesNotRollBackAlreadyInstalledFrontend()
    {
        _downloadService.ResultsByUrl[FrontendUrl] = new DownloadResult { Outcome = DownloadOutcome.Success, FilePath = _frontendArchivePath };
        _downloadService.ResultsByUrl[CoreUrl] = new DownloadResult { Outcome = DownloadOutcome.NetworkError, ErrorMessage = "core download failed" };
        var service = CreateService([MakeCatalogEntry()]);

        var result = await service.InstallAsync("nes");

        Assert.Equal(InstallOutcome.DownloadFailed, result.Outcome);
        var emulator = Assert.Single(_emulatorService.InstalledEmulators);
        Assert.True(File.Exists(emulator.ExecutablePath));
    }

    [Fact]
    public async Task InstallAsync_AlreadyInstalledFrontend_SkipsDownloadAndReusesExisting()
    {
        var existingDir = Path.Combine(_installDirectory, "retroarch");
        Directory.CreateDirectory(existingDir);
        var existingExePath = Path.Combine(existingDir, "retroarch.exe");
        File.WriteAllBytes(existingExePath, [9, 9, 9]);
        await _emulatorService.RegisterInstalledEmulatorAsync("retroarch", "RetroArch", existingExePath, "sha");

        _downloadService.ResultsByUrl[CoreUrl] = new DownloadResult { Outcome = DownloadOutcome.Success, FilePath = _coreArchivePath };
        var service = CreateService([MakeCatalogEntry()]);

        var result = await service.InstallAsync("nes");

        Assert.Equal(InstallOutcome.Success, result.Outcome);
        Assert.DoesNotContain(FrontendUrl, _downloadService.RequestedUrls);
        Assert.Single(_emulatorService.InstalledEmulators);
    }

    [Fact]
    public async Task InstallAsync_MultipleCoresForSamePlatform_UsesFirstDeterministicallyWithoutCrashing()
    {
        var entry = MakeCatalogEntry();
        entry.Cores.Add(new KnownEmulatorCore
        {
            Id = "nestopia",
            PlatformId = "nes",
            DownloadUrl = CoreUrl,
            Sha256 = "irrelevant",
            ExpectedSizeBytes = 3,
            CoreFileName = "fceumm_libretro.dll",
            CapturedAt = "2026-07-31"
        });
        _downloadService.ResultsByUrl[FrontendUrl] = new DownloadResult { Outcome = DownloadOutcome.Success, FilePath = _frontendArchivePath };
        _downloadService.ResultsByUrl[CoreUrl] = new DownloadResult { Outcome = DownloadOutcome.Success, FilePath = _coreArchivePath };
        var service = CreateService([entry]);

        var result = await service.InstallAsync("nes");

        Assert.Equal(InstallOutcome.Success, result.Outcome);
    }

    [Fact]
    public async Task InstallAsync_ReportsProgressStages()
    {
        _downloadService.ResultsByUrl[FrontendUrl] = new DownloadResult { Outcome = DownloadOutcome.Success, FilePath = _frontendArchivePath };
        _downloadService.ResultsByUrl[CoreUrl] = new DownloadResult { Outcome = DownloadOutcome.Success, FilePath = _coreArchivePath };
        var service = CreateService([MakeCatalogEntry()]);
        var progress = new SynchronousProgress<string>();

        await service.InstallAsync("nes", progress);

        Assert.Contains(progress.Reports, m => m.Contains("Downloading RetroArch"));
        Assert.Contains(progress.Reports, m => m.Contains("Extracting RetroArch"));
        Assert.Contains(progress.Reports, m => m.Contains("Downloading fceumm core"));
        Assert.Contains(progress.Reports, m => m.Contains("Extracting fceumm core"));
        Assert.Contains("Done.", progress.Reports);
    }

    [Fact]
    public async Task InstallAsync_CancelledDuringDownload_PropagatesOperationCanceledException()
    {
        _downloadService.DownloadGate = new TaskCompletionSource<DownloadResult>();
        var service = CreateService([MakeCatalogEntry()]);
        using var cts = new CancellationTokenSource();

        var installTask = service.InstallAsync("nes", null, cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => installTask);
    }
}
