using System.Text.RegularExpressions;
using EmuBridge.Exceptions;

namespace EmuBridge.Services;

// {Token} resolver for EmulatorProfile.ArgumentTemplate — see Decision #3 / ADR-4 for the full
// rationale (single-pass, not chained .Replace(), context-aware quoting). Shared between
// EmulatorService (validates at config save time) and LaunchService (validates again at launch
// time, and does the actual expansion) — two entry points into the same data, one validator.
public static class ArgumentTemplate
{
    public const string RomPathToken = "RomPath";
    public const string CorePathToken = "CorePath";

    private static readonly Regex TokenPattern = new(@"\{(\w+)\}", RegexOptions.Compiled);

    public static void Validate(string template)
    {
        if (!template.Contains($"{{{RomPathToken}}}"))
        {
            throw new EmuBridgeException(
                $"Argument template is missing the required '{{{RomPathToken}}}' token — " +
                "launching without it would start the emulator without a ROM, likely opening its main menu instead.");
        }
    }

    // corePath is optional — null for manually-configured profiles (ArgumentTemplate.CorePathToken
    // never appears in those templates). A template that references {CorePath} with no corePath
    // supplied still throws via the "unknown token" check below, same as any other undefined
    // token — not silently left as literal "{CorePath}" text in the launch arguments.
    public static string Expand(string template, string romPath, string? corePath = null)
    {
        Validate(template);

        var tokens = new Dictionary<string, string> { [RomPathToken] = romPath };
        if (corePath is not null)
        {
            tokens[CorePathToken] = corePath;
        }

        return TokenPattern.Replace(template, match =>
        {
            var tokenName = match.Groups[1].Value;
            if (!tokens.TryGetValue(tokenName, out var value))
            {
                throw new EmuBridgeException($"Unknown token '{{{tokenName}}}' in argument template.");
            }

            return QuoteIfNeeded(template, match, value);
        });
    }

    private static string QuoteIfNeeded(string template, Match match, string value)
    {
        var alreadyQuoted =
            match.Index > 0 && template[match.Index - 1] == '"' &&
            match.Index + match.Length < template.Length && template[match.Index + match.Length] == '"';

        var needsQuoting = value.Contains(' ') && !alreadyQuoted;

        return needsQuoting ? $"\"{value}\"" : value;
    }
}
