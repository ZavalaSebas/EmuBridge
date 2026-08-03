using System.IO;
using Bridge.Models;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace Bridge.Services;

// Orchestrates auto-installing a known emulator + core for a platform (ADR-14): download+verify
// (DownloadVerificationService, unchanged), extract (SharpCompress — pure managed, no native
// interop, same reasoning that avoided repeating ADR-12's mistake), register the resulting
// Emulator/EmulatorProfile via EmulatorService (never touches ILibraryRepository directly, per
// ADR-11's "EmulatorService is the sole consumer" invariant).
public class EmulatorInstallerService : IEmulatorInstallerService
{
    private readonly IDownloadVerificationService _downloadService;
    private readonly IEmulatorService _emulatorService;
    private readonly string _installDirectory;
    private readonly Func<IReadOnlyList<KnownEmulator>> _catalogProvider;
    private readonly ILogger<EmulatorInstallerService> _logger;

    public EmulatorInstallerService(IDownloadVerificationService downloadService, IEmulatorService emulatorService, IManifestUpdateService manifestUpdateService, ILogger<EmulatorInstallerService> logger)
        : this(downloadService, emulatorService, Config.EmulatorInstallPath, manifestUpdateService.GetCatalog, logger)
    {
    }

    // The catalog is a provider delegate, not a fixed snapshot, so this always sees whatever
    // IManifestUpdateService currently considers the best catalog (ARCHITECTURE.md -> ADR-25) —
    // this service is registered as a singleton, resolved once early at startup, so a fixed list
    // captured at construction time would never see a background refresh that completes later in
    // the same session. Also keeps tests able to point InstallAsync at real, small, hand-built
    // test archives instead of the actual KnownEmulators.json data (which would mean tests break
    // whenever the real catalog changes, and can't exercise failure paths like a corrupt/wrong
    // archive without an actual multi-hundred-MB download) — tests just pass `() => catalog`.
    public EmulatorInstallerService(
        IDownloadVerificationService downloadService,
        IEmulatorService emulatorService,
        string installDirectory,
        Func<IReadOnlyList<KnownEmulator>> catalogProvider,
        ILogger<EmulatorInstallerService> logger)
    {
        _downloadService = downloadService;
        _emulatorService = emulatorService;
        _installDirectory = installDirectory;
        _catalogProvider = catalogProvider;
        _logger = logger;
    }

    public Task<bool> HasKnownInstallOptionAsync(string platformId, CancellationToken ct = default)
    {
        var match = FindKnownCore(platformId);
        return Task.FromResult(match is not null && !IsUnverified(match.Value.KnownEmulator) && !IsUnverified(match.Value.Core));
    }

    public async Task<InstallResult> InstallAsync(string platformId, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var match = FindKnownCore(platformId);
        if (match is null)
        {
            return new InstallResult
            {
                Outcome = InstallOutcome.NoKnownCoreForPlatform,
                ErrorMessage = "No known emulator/core is available yet for this platform. Configure one manually in Settings."
            };
        }

        var (knownEmulator, core) = match.Value;

        if (IsUnverified(knownEmulator) || IsUnverified(core))
        {
            _logger.LogError("Refusing to install for platform {PlatformId}: catalog entry {KnownEmulatorId}/{CoreId} still has unverified placeholder data.", platformId, knownEmulator.Id, core.Id);
            return new InstallResult
            {
                Outcome = InstallOutcome.UnverifiedManifestData,
                ErrorMessage = "This emulator/core isn't verified yet and can't be installed. This is a Bridge bug, not something you can fix — please report it."
            };
        }

        Emulator emulator;
        var existing = await _emulatorService.GetInstalledKnownEmulatorAsync(knownEmulator.Id, ct);
        if (existing is not null && File.Exists(existing.ExecutablePath))
        {
            _logger.LogInformation("{KnownEmulatorId} already installed at {ExecutablePath}; reusing.", knownEmulator.Id, existing.ExecutablePath);
            emulator = existing;
        }
        else
        {
            var frontendStep = await InstallFrontendAsync(knownEmulator, progress, ct);
            if (frontendStep.Outcome != InstallOutcome.Success)
            {
                return new InstallResult { Outcome = frontendStep.Outcome, ErrorMessage = frontendStep.ErrorMessage };
            }

            emulator = await _emulatorService.RegisterInstalledEmulatorAsync(knownEmulator.Id, knownEmulator.Name, frontendStep.ResultPath!, knownEmulator.Sha256, ct);
        }

        var frontendDir = Path.GetDirectoryName(emulator.ExecutablePath)!;
        var coreStep = await InstallCoreAsync(core, frontendDir, progress, ct);
        if (coreStep.Outcome != InstallOutcome.Success)
        {
            // Deliberately does not roll back the frontend install above — a working frontend
            // install is a valid, reusable state even if this specific core failed (ADR-11's
            // whole point: one Emulator can back many platforms). Only the core's own partial
            // files are cleaned up, inside InstallCoreAsync itself.
            return new InstallResult { Outcome = coreStep.Outcome, ErrorMessage = coreStep.ErrorMessage };
        }

        var argumentTemplate = $"-L {{{ArgumentTemplate.CorePathToken}}} {{{ArgumentTemplate.RomPathToken}}}";
        await _emulatorService.RegisterCoreProfileAsync(platformId, emulator.Id, coreStep.ResultPath!, argumentTemplate, ct);

        progress?.Report("Done.");
        return new InstallResult { Outcome = InstallOutcome.Success };
    }

    private async Task<StepResult> InstallFrontendAsync(KnownEmulator knownEmulator, IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report($"Downloading {knownEmulator.Name}...");
        var downloadFileName = $"{knownEmulator.Id}-{knownEmulator.Version}{Path.GetExtension(knownEmulator.DownloadUrl)}";
        var downloadResult = await _downloadService.DownloadAndVerifyAsync(
            knownEmulator.DownloadUrl,
            downloadFileName,
            knownEmulator.Sha256,
            knownEmulator.ExpectedSizeBytes,
            CreateByteProgress(progress, knownEmulator.Name, knownEmulator.ExpectedSizeBytes),
            ct);

        if (downloadResult.Outcome != DownloadOutcome.Success)
        {
            return new StepResult(InstallOutcome.DownloadFailed, downloadResult.ErrorMessage, null);
        }

        progress?.Report($"Extracting {knownEmulator.Name}...");
        var extractDir = Path.Combine(_installDirectory, knownEmulator.Id);

        try
        {
            Directory.CreateDirectory(extractDir);
            using var archive = ArchiveFactory.OpenArchive(downloadResult.FilePath!);
            archive.WriteToDirectory(extractDir, new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to extract {KnownEmulatorId}.", knownEmulator.Id);
            DeleteDirectoryIfExists(extractDir);
            return new StepResult(InstallOutcome.ExtractionFailed, $"Extracting {knownEmulator.Name} failed. {ex.Message} It was not installed.", null);
        }

        var executablePath = Path.Combine(extractDir, knownEmulator.ExecutableRelativePath);
        if (!File.Exists(executablePath))
        {
            _logger.LogError("Expected executable not found at {Path} after extracting {KnownEmulatorId}.", executablePath, knownEmulator.Id);
            DeleteDirectoryIfExists(extractDir);
            return new StepResult(
                InstallOutcome.ExecutableNotFoundAfterExtraction,
                $"{knownEmulator.Name} was extracted, but its executable wasn't found where expected. This is a Bridge catalog bug, not something you can fix — please report it.",
                null);
        }

        return new StepResult(InstallOutcome.Success, null, executablePath);
    }

    private async Task<StepResult> InstallCoreAsync(KnownEmulatorCore core, string frontendDir, IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report($"Downloading {core.Id} core...");
        var downloadFileName = $"{core.Id}-{core.CapturedAt}{Path.GetExtension(core.DownloadUrl)}";
        var downloadResult = await _downloadService.DownloadAndVerifyAsync(
            core.DownloadUrl,
            downloadFileName,
            core.Sha256,
            core.ExpectedSizeBytes,
            CreateByteProgress(progress, $"{core.Id} core", core.ExpectedSizeBytes),
            ct);

        if (downloadResult.Outcome != DownloadOutcome.Success)
        {
            return new StepResult(InstallOutcome.DownloadFailed, downloadResult.ErrorMessage, null);
        }

        progress?.Report($"Extracting {core.Id} core...");
        var coresDir = Path.Combine(frontendDir, "cores");
        var corePath = Path.Combine(coresDir, core.CoreFileName);

        try
        {
            Directory.CreateDirectory(coresDir);
            using var archive = ArchiveFactory.OpenArchive(downloadResult.FilePath!);
            var entry = archive.Entries.FirstOrDefault(e =>
                !e.IsDirectory && e.Key is not null &&
                string.Equals(Path.GetFileName(e.Key), core.CoreFileName, StringComparison.OrdinalIgnoreCase));

            if (entry is null)
            {
                throw new InvalidOperationException($"Expected file '{core.CoreFileName}' was not found inside the downloaded archive.");
            }

            entry.WriteToDirectory(coresDir, new ExtractionOptions { ExtractFullPath = false, Overwrite = true });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to extract core {CoreId}.", core.Id);
            DeleteFileIfExists(corePath);
            return new StepResult(InstallOutcome.ExtractionFailed, $"Extracting the {core.Id} core failed. {ex.Message} It was not installed.", null);
        }

        if (!File.Exists(corePath))
        {
            DeleteFileIfExists(corePath);
            return new StepResult(
                InstallOutcome.ExecutableNotFoundAfterExtraction,
                $"The {core.Id} core was extracted, but wasn't found where expected. This is a Bridge catalog bug, not something you can fix — please report it.",
                null);
        }

        return new StepResult(InstallOutcome.Success, null, corePath);
    }

    private (KnownEmulator KnownEmulator, KnownEmulatorCore Core)? FindKnownCore(string platformId)
    {
        foreach (var emulator in _catalogProvider())
        {
            var matchingCores = emulator.Cores.Where(c => c.PlatformId == platformId).ToList();
            if (matchingCores.Count == 0)
            {
                continue;
            }

            if (matchingCores.Count > 1)
            {
                // No core-picker UI exists yet (ADR-14) — deterministic, not a crash, but flagged
                // loudly rather than silently picking one of several real options.
                _logger.LogWarning(
                    "Multiple KnownEmulatorCore entries found for platform {PlatformId} under {KnownEmulatorId}; using the first ({CoreId}). A core picker UI isn't built yet.",
                    platformId,
                    emulator.Id,
                    matchingCores[0].Id);
            }

            return (emulator, matchingCores[0]);
        }

        return null;
    }

    private static bool IsUnverified(KnownEmulator emulator) =>
        emulator.Sha256 == Config.UnverifiedManifestPlaceholder
        || emulator.DownloadUrl == Config.UnverifiedManifestPlaceholder
        || emulator.ExecutableRelativePath == Config.UnverifiedManifestPlaceholder;

    private static bool IsUnverified(KnownEmulatorCore core) =>
        core.Sha256 == Config.UnverifiedManifestPlaceholder
        || core.DownloadUrl == Config.UnverifiedManifestPlaceholder;

    private static IProgress<long>? CreateByteProgress(IProgress<string>? progress, string label, long expectedSizeBytes)
    {
        if (progress is null)
        {
            return null;
        }

        return new DelegateProgress<long>(bytesRead =>
        {
            var mb = bytesRead / (1024.0 * 1024.0);
            var totalMb = expectedSizeBytes / (1024.0 * 1024.0);
            progress.Report($"Downloading {label}... {mb:F0} / {totalMb:F0} MB");
        });
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed class DelegateProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }

    private record StepResult(InstallOutcome Outcome, string? ErrorMessage, string? ResultPath);
}
