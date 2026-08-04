using Bridge.Services;

namespace Bridge.Tests.Services;

public class CheatFileParserTests
{
    // Real content fetched from libretro/libretro-database (cht/Nintendo - Nintendo Entertainment
    // System/1943 - The Battle of Midway (USA) (Game Genie).cht) during design research — not a
    // hand-typed guess at the format.
    private const string RealNesCheatFile = """
        cheats = 3

        cheat0_desc = "10 Power Points"
        cheat0_code = "ZESNLLLE"
        cheat0_enable = false

        cheat1_desc = "20 Power Points"
        cheat1_code = "GOSNLLLA"
        cheat1_enable = false

        cheat2_desc = "Infinite Power"
        cheat2_code = "SXVLZXSE+VVOULXVK"
        cheat2_enable = true
        """;

    [Fact]
    public void Parse_RealLibretroDatabaseFile_ReturnsAllCheatsWithCorrectDescAndEnable()
    {
        var result = CheatFileParser.Parse(RealNesCheatFile);

        Assert.True(result.IsValid);
        Assert.Equal(3, result.Cheats.Count);
        Assert.Equal("10 Power Points", result.Cheats[0].Description);
        Assert.False(result.Cheats[0].Enabled);
        Assert.Equal("Infinite Power", result.Cheats[2].Description);
        Assert.True(result.Cheats[2].Enabled);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsInvalid()
    {
        var result = CheatFileParser.Parse(string.Empty);

        Assert.False(result.IsValid);
        Assert.Empty(result.Cheats);
    }

    [Fact]
    public void Parse_NoCheatsHeader_ReturnsInvalid()
    {
        var result = CheatFileParser.Parse("cheat0_desc = \"Something\"\ncheat0_enable = false");

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Parse_HeaderCountExceedsActualEntries_ReturnsInvalid()
    {
        // Claims 3 but only defines cheat0 - a truncated/corrupted file, not a valid empty tail.
        var text = """
            cheats = 3

            cheat0_desc = "Only One"
            cheat0_enable = false
            """;

        var result = CheatFileParser.Parse(text);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Parse_NonNumericCheatsHeader_ReturnsInvalid()
    {
        var result = CheatFileParser.Parse("cheats = abc\n");

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Parse_MissingEnableKeyForOneEntry_RejectsWholeFileNotPartialList()
    {
        var text = """
            cheats = 2

            cheat0_desc = "Has Both"
            cheat0_enable = true

            cheat1_desc = "Missing Enable"
            """;

        var result = CheatFileParser.Parse(text);

        // The whole file is rejected, not a 1-item partial list - never silently drop an entry
        // Bridge can't fully verify.
        Assert.False(result.IsValid);
        Assert.Empty(result.Cheats);
    }

    [Fact]
    public void Parse_ZeroCheats_ReturnsValidEmptyList()
    {
        var result = CheatFileParser.Parse("cheats = 0\n");

        Assert.True(result.IsValid);
        Assert.Empty(result.Cheats);
    }

    [Fact]
    public void SetEnabled_TogglesOnlyTheTargetLine_LeavesEverythingElseByteForByte()
    {
        var updated = CheatFileParser.SetEnabled(RealNesCheatFile, 0, true);

        // The specific line changed...
        var reparsed = CheatFileParser.Parse(updated);
        Assert.True(reparsed.IsValid);
        Assert.True(reparsed.Cheats[0].Enabled);

        // ...and nothing else did, including the untouched cheat1/cheat2 entries and every
        // cheatN_code value this parser never reads.
        Assert.False(reparsed.Cheats[1].Enabled);
        Assert.True(reparsed.Cheats[2].Enabled);
        Assert.Contains("cheat0_code = \"ZESNLLLE\"", updated);
        Assert.Contains("cheat2_code = \"SXVLZXSE+VVOULXVK\"", updated);

        // Exactly one line differs from the source (the patched cheat0_enable line) - proves the
        // patch is truly targeted, not a full reserialize that happens to produce equal content.
        var originalLines = RealNesCheatFile.Split('\n');
        var updatedLines = updated.Split('\n');
        Assert.Equal(originalLines.Length, updatedLines.Length);
        var differingLines = originalLines.Zip(updatedLines).Where(pair => pair.First != pair.Second).ToList();
        var onlyDiff = Assert.Single(differingLines);
        Assert.Equal("cheat0_enable = false", onlyDiff.First);
        Assert.Equal("cheat0_enable = true", onlyDiff.Second);
    }

    [Fact]
    public void SetEnabled_DisablingAnEnabledCheat_Works()
    {
        var updated = CheatFileParser.SetEnabled(RealNesCheatFile, 2, false);

        var reparsed = CheatFileParser.Parse(updated);
        Assert.True(reparsed.IsValid);
        Assert.False(reparsed.Cheats[2].Enabled);
    }

    [Fact]
    public void SetEnabled_IndexNotPresent_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => CheatFileParser.SetEnabled(RealNesCheatFile, 99, true));
    }

    // Real excerpt from a file RetroArch itself rewrote (confirmed via a real interactive session:
    // the user toggled cheats in RetroArch's own menu, which triggers
    // cheat_manager_save_game_specific_cheats, overwriting Bridge's file). RetroArch's writer
    // quotes every value including booleans (cheat0_enable = "true"), and its config serializer
    // sorts keys alphabetically by string, so the "cheats = N" header - which starts with a
    // non-digit character - sorts *after* every "cheatN_..." entry and lands near the end of the
    // file, not the start. This broke the original parser (only tolerated bare true/false), found
    // by reading the actual file that produced a real "corrupted" report, not assumed.
    private const string RetroArchRewrittenExcerpt = """
        cheat0_address = "0"
        cheat0_address_bit_position = "0"
        cheat0_big_endian = "false"
        cheat0_cheat_type = "1"
        cheat0_code = "7E0DBF63"
        cheat0_desc = "Infinite Maximum Coins"
        cheat0_enable = "true"
        cheat0_handler = "0"
        cheat1_address = "0"
        cheat1_address_bit_position = "0"
        cheat1_big_endian = "false"
        cheat1_cheat_type = "1"
        cheat1_code = "0032-6DAD"
        cheat1_desc = "&quot;Gem&quot; Mario"
        cheat1_enable = "false"
        cheat1_handler = "0"
        cheats = "2"
        """;

    [Fact]
    public void Parse_RetroArchRewrittenFormatWithQuotedBooleansAndHeaderAtEnd_ParsesCorrectly()
    {
        var result = CheatFileParser.Parse(RetroArchRewrittenExcerpt);

        Assert.True(result.IsValid);
        Assert.Equal(2, result.Cheats.Count);
        Assert.Equal("Infinite Maximum Coins", result.Cheats[0].Description);
        Assert.True(result.Cheats[0].Enabled);
        Assert.Equal("&quot;Gem&quot; Mario", result.Cheats[1].Description);
        Assert.False(result.Cheats[1].Enabled);
    }

    [Fact]
    public void SetEnabled_OnRetroArchRewrittenQuotedFormat_TogglesTheQuotedValueCorrectly()
    {
        var updated = CheatFileParser.SetEnabled(RetroArchRewrittenExcerpt, 1, true);

        var reparsed = CheatFileParser.Parse(updated);
        Assert.True(reparsed.IsValid);
        Assert.True(reparsed.Cheats[1].Enabled);
        // Quoting is preserved, not stripped - the patch only replaces the word inside the quotes.
        Assert.Contains("cheat1_enable = \"true\"", updated);
    }
}
