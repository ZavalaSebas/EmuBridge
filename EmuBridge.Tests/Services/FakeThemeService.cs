using EmuBridge.Models;
using EmuBridge.Services;

namespace EmuBridge.Tests.Services;

internal class FakeThemeService : IThemeService
{
    public ThemePreference CurrentTheme { get; private set; } = ThemePreference.System;

    public ThemePreference? LastAppliedTheme { get; private set; }

    public void ApplyTheme(ThemePreference preference)
    {
        LastAppliedTheme = preference;
        CurrentTheme = preference;
    }

    public void ApplyPersistedTheme()
    {
    }
}
