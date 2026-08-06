using System.IO;
using System.Text.Json;
using EmuBridge.Models;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace EmuBridge.Services;

public interface IThemeService
{
    // The theme currently applied to the app. Set by ApplyTheme/ApplyPersistedTheme; read by the
    // Settings view so its dropdown reflects reality even if the app was launched on a persisted
    // theme without opening Settings.
    ThemePreference CurrentTheme { get; }

    // Applies a theme immediately (swaps WPF-UI's resource dictionaries). Safe to call from a
    // background thread — ApplicationThemeManager marshals the resource swap onto the UI thread.
    void ApplyTheme(ThemePreference preference);

    // Reads the persisted theme from settings.json synchronously and applies it. Deliberately
    // synchronous: called from App.OnStartup before the main window is created, so the first
    // frame already shows the user's chosen theme instead of a flash of the system default.
    void ApplyPersistedTheme();
}

public class ThemeService : IThemeService
{
    private readonly string _settingsPath;
    private readonly ILogger<ThemeService> _logger;

    public ThemeService(ILogger<ThemeService> logger)
        : this(Config.SettingsPath, logger)
    {
    }

    public ThemeService(string settingsPath, ILogger<ThemeService> logger)
    {
        _settingsPath = settingsPath;
        _logger = logger;
    }

    public ThemePreference CurrentTheme { get; private set; } = ThemePreference.System;

    public void ApplyTheme(ThemePreference preference)
    {
        switch (preference)
        {
            case ThemePreference.Light:
                ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.Mica, true);
                break;
            case ThemePreference.Dark:
                ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Mica, true);
                break;
            default:
                // System: follow whatever Windows is actually set to — the explicit choice made
                // when WPF-UI was integrated (ADR-29), not a fixed Light/Dark fallback.
                ApplicationThemeManager.ApplySystemTheme();
                break;
        }

        CurrentTheme = preference;
    }

    public void ApplyPersistedTheme()
    {
        ApplyTheme(ReadPersistedTheme());
    }

    // Read-only peek at settings.json (SettingsService is the only writer — this never touches
    // any field but the theme one, so it can't clobber the API keys or cheats toggle). Parsed
    // leniently: a missing/corrupt file or an unknown enum string just falls back to System,
    // matching the "never set" default — a theme glitch must never block startup.
    private ThemePreference ReadPersistedTheme()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return ThemePreference.System;
            }

            using var stream = File.OpenRead(_settingsPath);
            var settings = JsonSerializer.Deserialize<SettingsService.SettingsFile>(stream);
            return settings?.Theme ?? ThemePreference.System;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _logger.LogWarning(ex, "Could not read the persisted theme from {SettingsPath}; using System theme.", _settingsPath);
            return ThemePreference.System;
        }
    }
}
