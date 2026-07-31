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
