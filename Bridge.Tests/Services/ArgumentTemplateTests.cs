using Bridge.Exceptions;
using Bridge.Services;

namespace Bridge.Tests.Services;

public class ArgumentTemplateTests
{
    [Fact]
    public void Validate_TemplateWithRomPathToken_DoesNotThrow()
    {
        var exception = Record.Exception(() => ArgumentTemplate.Validate("\"{RomPath}\""));

        Assert.Null(exception);
    }

    [Fact]
    public void Validate_TemplateWithoutRomPathToken_ThrowsBridgeException()
    {
        Assert.Throws<BridgeException>(() => ArgumentTemplate.Validate("-fullscreen"));
    }

    [Fact]
    public void Expand_ReplacesRomPathToken()
    {
        var result = ArgumentTemplate.Expand("-L cores\\core.dll {RomPath}", @"C:\roms\mario.nes");

        Assert.Equal(@"-L cores\core.dll C:\roms\mario.nes", result);
    }

    [Fact]
    public void Expand_ValueWithSpaces_GetsAutoQuoted()
    {
        var result = ArgumentTemplate.Expand("{RomPath}", @"C:\My Roms\mario.nes");

        Assert.Equal("\"C:\\My Roms\\mario.nes\"", result);
    }

    [Fact]
    public void Expand_AlreadyManuallyQuotedToken_DoesNotDoubleQuote()
    {
        var result = ArgumentTemplate.Expand("\"{RomPath}\"", @"C:\My Roms\mario.nes");

        Assert.Equal("\"C:\\My Roms\\mario.nes\"", result);
    }

    [Fact]
    public void Expand_ValueWithoutSpaces_IsNotQuoted()
    {
        var result = ArgumentTemplate.Expand("{RomPath}", @"C:\roms\mario.nes");

        Assert.Equal(@"C:\roms\mario.nes", result);
    }

    [Fact]
    public void Expand_UnknownToken_ThrowsBridgeException()
    {
        Assert.Throws<BridgeException>(() => ArgumentTemplate.Expand("{RomPath} {Unknown}", @"C:\roms\mario.nes"));
    }

    [Fact]
    public void Expand_MissingRequiredToken_ThrowsBridgeException()
    {
        Assert.Throws<BridgeException>(() => ArgumentTemplate.Expand("-fullscreen", @"C:\roms\mario.nes"));
    }

    [Fact]
    public void Expand_CorePathSupplied_ReplacesCorePathToken()
    {
        var result = ArgumentTemplate.Expand("-L {CorePath} {RomPath}", @"C:\roms\mario.nes", @"C:\emu\cores\core.dll");

        Assert.Equal(@"-L C:\emu\cores\core.dll C:\roms\mario.nes", result);
    }

    [Fact]
    public void Expand_CorePathNotSupplied_TemplateWithoutCorePathToken_Unaffected()
    {
        var result = ArgumentTemplate.Expand("{RomPath}", @"C:\roms\mario.nes");

        Assert.Equal(@"C:\roms\mario.nes", result);
    }

    [Fact]
    public void Expand_CorePathTokenInTemplateButNoCorePathSupplied_ThrowsBridgeException()
    {
        Assert.Throws<BridgeException>(() => ArgumentTemplate.Expand("-L {CorePath} {RomPath}", @"C:\roms\mario.nes"));
    }
}
