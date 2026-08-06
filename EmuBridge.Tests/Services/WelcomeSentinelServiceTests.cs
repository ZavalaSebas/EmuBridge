using System.IO;
using EmuBridge.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmuBridge.Tests.Services;

public class WelcomeSentinelServiceTests : IDisposable
{
    private readonly string _sentinelPath;
    private readonly WelcomeSentinelService _service;

    public WelcomeSentinelServiceTests()
    {
        _sentinelPath = Path.Combine(Path.GetTempPath(), $"emubridge_welcome_test_{Guid.NewGuid()}.txt");
        _service = new WelcomeSentinelService(_sentinelPath, "1.0.0", NullLogger<WelcomeSentinelService>.Instance);
    }

    public void Dispose()
    {
        if (File.Exists(_sentinelPath))
        {
            File.Delete(_sentinelPath);
        }
    }

    [Fact]
    public void ShouldShowWelcome_NoSentinelYet_ReturnsTrue()
    {
        Assert.True(_service.ShouldShowWelcome());
    }

    [Fact]
    public void ShouldShowWelcome_AfterMarkShown_ReturnsFalse()
    {
        _service.MarkWelcomeShown();

        Assert.False(_service.ShouldShowWelcome());
    }

    [Fact]
    public void ShouldShowWelcome_WhenVersionChanged_ReturnsTrue()
    {
        _service.MarkWelcomeShown();

        var newerService = new WelcomeSentinelService(_sentinelPath, "1.1.0", NullLogger<WelcomeSentinelService>.Instance);

        Assert.True(newerService.ShouldShowWelcome());
    }

    [Fact]
    public void MarkWelcomeShown_WritesCurrentVersionToFile()
    {
        _service.MarkWelcomeShown();

        Assert.Equal("1.0.0", File.ReadAllText(_sentinelPath).Trim());
    }

    [Fact]
    public void ShouldShowWelcome_CorruptSentinelFile_ShowsWelcome()
    {
        File.WriteAllText(_sentinelPath, "\u0000\u0001garbage-not-a-version");

        Assert.True(_service.ShouldShowWelcome());
    }
}
