using System.IO;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace EmuBridge.Services;

public interface IWelcomeSentinelService
{
    // True on first run and after a version change (the sentinel file's stored version differs
    // from the running one); false on repeat launches of the same version.
    bool ShouldShowWelcome();

    // Records the running version in the sentinel file so the welcome won't reappear until the
    // next version change.
    void MarkWelcomeShown();
}

public class WelcomeSentinelService : IWelcomeSentinelService
{
    private readonly string _sentinelPath;
    private readonly string _currentVersion;
    private readonly ILogger<WelcomeSentinelService> _logger;

    public WelcomeSentinelService(ILogger<WelcomeSentinelService> logger)
        : this(Config.WelcomeSentinelPath, CurrentAssemblyVersion(), logger)
    {
    }

    public WelcomeSentinelService(string sentinelPath, string currentVersion, ILogger<WelcomeSentinelService> logger)
    {
        _sentinelPath = sentinelPath;
        _currentVersion = currentVersion;
        _logger = logger;
    }

    // Best-effort; reads only (the file is written by MarkWelcomeShown below, a single writer).
    public bool ShouldShowWelcome()
    {
        try
        {
            if (!File.Exists(_sentinelPath))
            {
                return true; // first run — nothing has ever been marked shown
            }

            var lastShownVersion = File.ReadAllText(_sentinelPath).Trim();
            return lastShownVersion != _currentVersion;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not read the welcome sentinel at {SentinelPath}; treating as not shown.", _sentinelPath);
            return true;
        }
    }

    public void MarkWelcomeShown()
    {
        try
        {
            var directory = Path.GetDirectoryName(_sentinelPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_sentinelPath, _currentVersion);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Never fail startup (or the close of the welcome dialog) over a bookkeeping file —
            // the worst case is the welcome showing again next launch, which is recoverable.
            _logger.LogWarning(ex, "Could not write the welcome sentinel at {SentinelPath}.", _sentinelPath);
        }
    }

    private static string CurrentAssemblyVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version?.ToString(3) ?? "0.0.0";
    }
}
