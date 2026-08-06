namespace EmuBridge.Models;

// User-facing theme choice (Phase Polish -> "Theme customization / visual personalization").
// System is the default — EmuBridge follows the real Windows theme (the WPF-UI integration's
// original decision, ARCHITECTURE.md -> ADR-29) unless the user explicitly picks Light or Dark.
public enum ThemePreference
{
    System = 0,
    Light = 1,
    Dark = 2
}
