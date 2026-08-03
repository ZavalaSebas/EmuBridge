using System.Text.RegularExpressions;

namespace ManifestDriftCheck;

// Targeted, line-based text replacement — deliberately not deserialize/reserialize the whole
// file. Preserves everything untouched by a given entry's drift (formatting, key order, entries
// that didn't change) so the resulting diff shows only real data changes, matching the manual
// edits already made three times by hand on 2026-08-02 (ARCHITECTURE.md -> ADR-11). Only ever
// applied to entries classified EntryStatus.Drifted — a structural change is never patched here.
public static partial class ManifestPatcher
{
    public record EntryUpdate(string Id, string NewSha256, long NewExpectedSizeBytes, string? NewCapturedAt);

    public static string ApplyUpdates(string rawJson, IReadOnlyList<EntryUpdate> updates)
    {
        if (updates.Count == 0)
        {
            return rawJson;
        }

        var byId = updates.ToDictionary(u => u.Id);
        var lines = rawJson.Split('\n');
        string? activeId = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var idMatch = IdLine().Match(lines[i]);
            if (idMatch.Success)
            {
                var id = idMatch.Groups[2].Value;
                activeId = byId.ContainsKey(id) ? id : null;
                continue;
            }

            if (activeId is null)
            {
                continue;
            }

            var update = byId[activeId];

            var shaMatch = Sha256Line().Match(lines[i]);
            if (shaMatch.Success)
            {
                lines[i] = $"{shaMatch.Groups[1].Value}{update.NewSha256}{shaMatch.Groups[2].Value}";
                continue;
            }

            var sizeMatch = SizeLine().Match(lines[i]);
            if (sizeMatch.Success)
            {
                lines[i] = $"{sizeMatch.Groups[1].Value}{update.NewExpectedSizeBytes}{sizeMatch.Groups[2].Value}";
                continue;
            }

            if (update.NewCapturedAt is not null)
            {
                var capturedMatch = CapturedAtLine().Match(lines[i]);
                if (capturedMatch.Success)
                {
                    lines[i] = $"{capturedMatch.Groups[1].Value}{update.NewCapturedAt}{capturedMatch.Groups[2].Value}";
                }
            }
        }

        return string.Join('\n', lines);
    }

    // Each pattern captures a prefix (up to and including the opening quote, where applicable)
    // and a suffix (from the closing quote/comma onward) — the value in between gets replaced,
    // everything else on the line (indentation, trailing comma, line ending) is preserved as-is.
    [GeneratedRegex("""^(\s*"Id":\s*")([^"]+)(".*)$""")]
    private static partial Regex IdLine();

    [GeneratedRegex("""^(\s*"Sha256":\s*")[^"]*(".*)$""")]
    private static partial Regex Sha256Line();

    [GeneratedRegex("""^(\s*"ExpectedSizeBytes":\s*)\d+(.*)$""")]
    private static partial Regex SizeLine();

    [GeneratedRegex("""^(\s*"CapturedAt":\s*")[^"]*(".*)$""")]
    private static partial Regex CapturedAtLine();
}
