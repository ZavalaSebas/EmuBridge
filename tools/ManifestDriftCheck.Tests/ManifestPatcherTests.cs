namespace ManifestDriftCheck.Tests;

public class ManifestPatcherTests
{
    private const string SampleJson = """
        [
          {
            "Id": "retroarch",
            "Name": "RetroArch",
            "Version": "1.22.2",
            "DownloadUrl": "https://buildbot.libretro.com/stable/1.22.2/windows/x86_64/RetroArch.7z",
            "Sha256": "b2139b1d0f9d4526dc6b5ce23cbb3efdc766096fa6f2c3df016818b486ac6372",
            "ExpectedSizeBytes": 202509078,
            "ExecutableRelativePath": "RetroArch-Win64\\retroarch.exe",
            "Cores": [
              {
                "Id": "fceumm",
                "PlatformId": "nes",
                "DownloadUrl": "https://buildbot.libretro.com/nightly/windows/x86_64/latest/fceumm_libretro.dll.zip",
                "Sha256": "9382bc1bb33eaf6a2b5d18320fa7312fcca95874bd26539654e5296b84260020",
                "ExpectedSizeBytes": 635595,
                "CoreFileName": "fceumm_libretro.dll",
                "CapturedAt": "2026-07-31"
              },
              {
                "Id": "snes9x",
                "PlatformId": "snes",
                "DownloadUrl": "https://buildbot.libretro.com/nightly/windows/x86_64/latest/snes9x_libretro.dll.zip",
                "Sha256": "3e26cd5cc26d9d2ceb9c35fe91026dd56dd667e3cfab28653421de0c79da4156",
                "ExpectedSizeBytes": 969800,
                "CoreFileName": "snes9x_libretro.dll",
                "CapturedAt": "2026-07-31"
              }
            ]
          }
        ]
        """;

    [Fact]
    public void ApplyUpdates_NoUpdates_ReturnsInputUnchanged()
    {
        var result = ManifestPatcher.ApplyUpdates(SampleJson, []);

        Assert.Equal(SampleJson, result);
    }

    [Fact]
    public void ApplyUpdates_CoreDrift_UpdatesOnlySha256SizeAndCapturedAt()
    {
        var update = new ManifestPatcher.EntryUpdate("fceumm", "newhash123", 999999, "2026-08-02");

        var result = ManifestPatcher.ApplyUpdates(SampleJson, [update]);

        Assert.Contains("\"Sha256\": \"newhash123\",", result);
        Assert.Contains("\"ExpectedSizeBytes\": 999999,", result);
        Assert.Contains("\"CapturedAt\": \"2026-08-02\"", result);
        // Untouched sibling entry — proves the patch stayed scoped to the target entry only.
        Assert.Contains("\"Sha256\": \"3e26cd5cc26d9d2ceb9c35fe91026dd56dd667e3cfab28653421de0c79da4156\",", result);
        Assert.Contains("\"CapturedAt\": \"2026-07-31\"", result);
    }

    [Fact]
    public void ApplyUpdates_FrontendDrift_UpdatesSha256AndSizeWithNoCapturedAtField()
    {
        // RetroArch's top-level entry has no CapturedAt field at all — NewCapturedAt is null.
        var update = new ManifestPatcher.EntryUpdate("retroarch", "newfrontendhash", 111222333, null);

        var result = ManifestPatcher.ApplyUpdates(SampleJson, [update]);

        Assert.Contains("\"Sha256\": \"newfrontendhash\",", result);
        Assert.Contains("\"ExpectedSizeBytes\": 111222333,", result);
        // Untouched — proves the patcher didn't try (and fail) to find a nonexistent CapturedAt
        // field for an entry whose EntryUpdate.NewCapturedAt was null.
        Assert.Contains("\"ExecutableRelativePath\": \"RetroArch-Win64\\\\retroarch.exe\",", result);
    }

    [Fact]
    public void ApplyUpdates_MultipleEntriesSharingAUrl_BothUpdatedIndependently()
    {
        var updates = new[]
        {
            new ManifestPatcher.EntryUpdate("fceumm", "hash-a", 111, "2026-08-02"),
            new ManifestPatcher.EntryUpdate("snes9x", "hash-b", 222, "2026-08-02")
        };

        var result = ManifestPatcher.ApplyUpdates(SampleJson, updates);

        Assert.Contains("\"Sha256\": \"hash-a\",", result);
        Assert.Contains("\"ExpectedSizeBytes\": 111,", result);
        Assert.Contains("\"Sha256\": \"hash-b\",", result);
        Assert.Contains("\"ExpectedSizeBytes\": 222,", result);
    }

    [Fact]
    public void ApplyUpdates_UnknownId_LeavesFileUnchanged()
    {
        var update = new ManifestPatcher.EntryUpdate("does-not-exist", "hash", 1, "2026-08-02");

        var result = ManifestPatcher.ApplyUpdates(SampleJson, [update]);

        Assert.Equal(SampleJson, result);
    }

    [Fact]
    public void ApplyUpdates_PreservesLineCountAndIndentation()
    {
        var update = new ManifestPatcher.EntryUpdate("fceumm", "newhash", 1, "2026-08-02");

        var result = ManifestPatcher.ApplyUpdates(SampleJson, [update]);

        Assert.Equal(SampleJson.Split('\n').Length, result.Split('\n').Length);
        Assert.Contains("        \"Sha256\": \"newhash\",", result); // 8-space indent preserved
    }
}
