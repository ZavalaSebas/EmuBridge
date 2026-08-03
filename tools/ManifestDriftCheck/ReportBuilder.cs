using System.Text;

namespace ManifestDriftCheck;

public static class ReportBuilder
{
    public static string Build(IReadOnlyList<CheckResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("| Entry | Expected Size | Real Size | Expected Hash | Real Hash | Status |");
        sb.AppendLine("|---|---|---|---|---|---|");

        foreach (var result in results)
        {
            var actualSize = result.ActualSizeBytes?.ToString() ?? "—";
            var expectedHash = Shorten(result.Target.ExpectedSha256);
            var actualHash = result.ActualSha256 is null ? "—" : Shorten(result.ActualSha256);
            var status = result.Status switch
            {
                EntryStatus.Match => "✅ match",
                EntryStatus.Drifted => "🔄 drifted — applied",
                EntryStatus.StructuralChange => "⚠️ structure changed — needs manual review, not applied",
                EntryStatus.VerificationFailed => $"❌ could not verify — {result.Detail}",
                _ => "?"
            };

            sb.AppendLine($"| {result.Target.DisplayLabel} | {result.Target.ExpectedSizeBytes} | {actualSize} | `{expectedHash}` | `{actualHash}` | {status} |");
        }

        var driftedCount = results.Count(r => r.Status == EntryStatus.Drifted);
        var attentionCount = results.Count(r => r.Status is EntryStatus.StructuralChange or EntryStatus.VerificationFailed);

        sb.AppendLine();
        sb.AppendLine($"{driftedCount} entr{(driftedCount == 1 ? "y" : "ies")} drifted and applied. " +
                       $"{attentionCount} need{(attentionCount == 1 ? "s" : "")} manual attention (not applied automatically).");

        return sb.ToString();
    }

    private static string Shorten(string hash) => hash.Length <= 12 ? hash : hash[..12] + "…";
}
