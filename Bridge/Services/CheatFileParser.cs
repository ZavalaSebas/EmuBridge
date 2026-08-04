using System.Text.RegularExpressions;
using Bridge.Models;

namespace Bridge.Services;

// Parses/patches RetroArch .cht files (ARCHITECTURE.md -> ADR-27) — plain-text key=value pairs,
// format confirmed against a real file fetched from libretro/libretro-database, not assumed from
// a third-party guide. Bridge only ever needs cheatN_desc/cheatN_enable per entry to show a
// toggleable list; cheatN_code and any handler-specific keys (cheatN_address, cheatN_handler,
// etc., used by "RetroArch Handled" cheats — see docs.libretro.com/guides/cheat-codes) are left
// completely untouched. SetEnabled patches just the one cheatN_enable line in place, the same
// targeted-text-patch discipline ManifestPatcher already uses for KnownEmulators.json, rather than
// parsing/regenerating the whole file and risking silently dropping a key this parser never reads.
public static class CheatFileParser
{
    private static readonly Regex CountPattern = new(@"^\s*cheats\s*=\s*""?(\d+)""?\s*$", RegexOptions.Multiline);

    public static CheatParseResult Parse(string rawText)
    {
        var countMatch = CountPattern.Match(rawText);
        if (!countMatch.Success || !int.TryParse(countMatch.Groups[1].Value, out var count) || count < 0)
        {
            return new CheatParseResult(false, []);
        }

        var cheats = new List<Cheat>(count);
        for (var i = 0; i < count; i++)
        {
            var descMatch = DescPattern(i).Match(rawText);
            var enableMatch = EnablePattern(i).Match(rawText);

            if (!descMatch.Success || !enableMatch.Success)
            {
                // Reject the whole file rather than show a partial list — an entry Bridge can't
                // find in full might mean the "cheats = N" header doesn't actually describe what
                // follows, and a silently-incomplete list is worse than an explicit "couldn't read
                // this file" (same standard DownloadVerificationService already holds downloads to
                // — never partially trust something that doesn't fully verify).
                return new CheatParseResult(false, []);
            }

            cheats.Add(new Cheat
            {
                Index = i,
                Description = descMatch.Groups[1].Value,
                Enabled = bool.Parse(enableMatch.Groups[1].Value)
            });
        }

        return new CheatParseResult(true, cheats);
    }

    // Targeted single-line patch, matching ManifestPatcher's precedent — every other line,
    // including keys this parser never reads, stays byte-for-byte untouched. Only ever called
    // after a successful Parse of the same text (CheatService's contract), so a missing match here
    // means a caller bug, not a user-facing state — throws rather than failing silently.
    public static string SetEnabled(string rawText, int index, bool enabled)
    {
        var match = EnablePattern(index).Match(rawText);
        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"cheat{index}_enable not found in the provided text — SetEnabled must only be called after a successful Parse of the same text.");
        }

        var valueGroup = match.Groups[1];
        var replacement = enabled ? "true" : "false";
        return rawText[..valueGroup.Index] + replacement + rawText[(valueGroup.Index + valueGroup.Length)..];
    }

    private static Regex DescPattern(int index) =>
        new($@"^\s*cheat{index}_desc\s*=\s*""([^""]*)""", RegexOptions.Multiline);

    // Quotes around true/false are optional - libretro-database's own distributed files write
    // this bare (cheat0_enable = false), but RetroArch's own save routine (triggered whenever the
    // user toggles a cheat in RetroArch's own menu, confirmed via a real file RetroArch rewrote)
    // quotes every value it writes, including booleans (cheat0_enable = "true"). Real evidence,
    // not assumed: found by reading the actual file after a real interactive session reported
    // "corrupted" for a file that RetroArch itself had just successfully written.
    private static Regex EnablePattern(int index) =>
        new($@"^\s*cheat{index}_enable\s*=\s*""?(true|false)""?", RegexOptions.Multiline);
}

public record CheatParseResult(bool IsValid, IReadOnlyList<Cheat> Cheats);
