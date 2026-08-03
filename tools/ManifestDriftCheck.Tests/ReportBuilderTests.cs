namespace ManifestDriftCheck.Tests;

public class ReportBuilderTests
{
    private static CheckTarget MakeTarget(string id = "fceumm") =>
        new(id, $"{id} (nes)", "https://example.com/core.zip", "expectedhash1234", 1000, "core.dll", HasCapturedAt: true);

    [Fact]
    public void Build_MatchEntry_ShowsMatchStatus()
    {
        var results = new[] { new CheckResult(MakeTarget(), EntryStatus.Match, 1000, "expectedhash1234", null) };

        var report = ReportBuilder.Build(results);

        Assert.Contains("✅ match", report);
        Assert.Contains("0 entries drifted and applied", report);
    }

    [Fact]
    public void Build_DriftedEntry_ShowsDriftedStatusAndCountsIt()
    {
        var results = new[] { new CheckResult(MakeTarget(), EntryStatus.Drifted, 1000, "differenthash", null) };

        var report = ReportBuilder.Build(results);

        Assert.Contains("🔄 drifted — applied", report);
        Assert.Contains("1 entry drifted and applied", report);
    }

    [Fact]
    public void Build_StructuralChangeEntry_ShowsWarningAndCountsAsNeedingAttention()
    {
        var results = new[]
        {
            new CheckResult(MakeTarget(), EntryStatus.StructuralChange, 1000, "somehash", "Expected path 'core.dll' was not found inside the archive.")
        };

        var report = ReportBuilder.Build(results);

        Assert.Contains("⚠️ structure changed", report);
        Assert.Contains("1 needs manual attention", report);
    }

    [Fact]
    public void Build_VerificationFailedEntry_ShowsErrorDetailAndCountsAsNeedingAttention()
    {
        var results = new[]
        {
            new CheckResult(MakeTarget(), EntryStatus.VerificationFailed, null, null, "Connection timed out")
        };

        var report = ReportBuilder.Build(results);

        Assert.Contains("❌ could not verify — Connection timed out", report);
        Assert.Contains("—", report); // real size/hash columns render as em-dash when null
        Assert.Contains("1 needs manual attention", report);
    }

    [Fact]
    public void Build_LongHash_IsShortenedForReadability()
    {
        var target = MakeTarget() with { ExpectedSha256 = "abcdefabcdefabcdefabcdefabcdef" };
        var results = new[] { new CheckResult(target, EntryStatus.Match, 1000, "abcdefabcdefabcdefabcdefabcdef", null) };

        var report = ReportBuilder.Build(results);

        Assert.Contains("abcdefabcdef…", report);
        Assert.DoesNotContain("abcdefabcdefabcdefabcdefabcdef", report);
    }
}
