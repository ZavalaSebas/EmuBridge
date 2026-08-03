using System.Reflection;
using System.Text.Json;
using Bridge.Models;

namespace Bridge.Tests.Services;

public class KnownEmulatorsManifestTests
{
    private static List<KnownEmulator> LoadManifest()
    {
        var assembly = typeof(Config).Assembly;
        using var stream = assembly.GetManifestResourceStream(Config.KnownEmulatorsResourceName);
        Assert.NotNull(stream);
        return JsonSerializer.Deserialize<List<KnownEmulator>>(stream!) ?? [];
    }

    [Fact]
    public void KnownEmulators_ManifestParses()
    {
        var manifest = LoadManifest();

        Assert.NotEmpty(manifest);
    }

    // ARCHITECTURE.md -> ADR-26: catches an untrusted DownloadUrl being added to the manifest at
    // build/test time, not in production against a real user. Not Release-gated like the
    // placeholder guard below — there's no "still being sourced" grace period for a trusted-host
    // violation the way there is for placeholder data.
    [Fact]
    public void KnownEmulators_AllDownloadUrlsUseAnAllowedHost()
    {
        var manifest = LoadManifest();

        foreach (var emulator in manifest)
        {
            AssertAllowedHost(emulator.DownloadUrl, emulator.Id);
            foreach (var core in emulator.Cores)
            {
                AssertAllowedHost(core.DownloadUrl, core.Id);
            }
        }
    }

    // Placeholder entries are skipped, not failed here — a Debug build is explicitly allowed to
    // carry Config.UnverifiedManifestPlaceholder while an entry is still being sourced (see the
    // Release-gated test below); "is a sentinel string a trusted host" isn't a meaningful question
    // to ask about that entry yet, so this test stays silent on it rather than double-punishing it.
    private static void AssertAllowedHost(string downloadUrl, string entryId)
    {
        if (downloadUrl == Config.UnverifiedManifestPlaceholder)
        {
            return;
        }

        Assert.True(Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri), $"{entryId}: '{downloadUrl}' is not a valid absolute URL.");
        Assert.True(Config.AllowedDownloadHosts.Contains(uri!.Host), $"{entryId}: host '{uri.Host}' is not in Config.AllowedDownloadHosts.");
        Assert.Equal(Uri.UriSchemeHttps, uri.Scheme);
    }

    // Gated to Release builds only — matches the existing release gate (DEVELOPMENT.md's
    // checklist runs `dotnet test Bridge.slnx -c Release`). A Debug build is allowed to carry
    // Config.UnverifiedManifestPlaceholder entries while emulator/core data is still being
    // sourced and verified one at a time; a Release build must not. See ARCHITECTURE.md -> ADR-11.
#if RELEASE
    [Fact]
    public void KnownEmulators_NoUnverifiedPlaceholdersInReleaseBuild()
    {
        var manifest = LoadManifest();

        foreach (var emulator in manifest)
        {
            Assert.NotEqual(Config.UnverifiedManifestPlaceholder, emulator.Sha256);
            Assert.NotEqual(Config.UnverifiedManifestPlaceholder, emulator.DownloadUrl);
            Assert.NotEqual(Config.UnverifiedManifestPlaceholder, emulator.ExecutableRelativePath);

            foreach (var core in emulator.Cores)
            {
                Assert.NotEqual(Config.UnverifiedManifestPlaceholder, core.Sha256);
                Assert.NotEqual(Config.UnverifiedManifestPlaceholder, core.DownloadUrl);
            }
        }
    }
#endif
}
