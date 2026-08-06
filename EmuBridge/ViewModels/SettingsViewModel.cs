using System.Collections.ObjectModel;
using System.Windows;
using EmuBridge.Exceptions;
using EmuBridge.Models;
using EmuBridge.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace EmuBridge.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ILibraryRepository _libraryRepository;
    private readonly IEmulatorService _emulatorService;
    private readonly IEmulatorInstallerService _installerService;
    private readonly ISettingsService _settingsService;
    private readonly IFilePickerService _filePickerService;
    private readonly IMessageBoxService _messageBoxService;
    private readonly IThemeService _themeService;
    private readonly IUpdateService _updateService;

    private CancellationTokenSource? _installCts;

    // Guards the On*Changed partial hooks below so InitializeAsync's own load of persisted values
    // never triggers a spurious write/apply — only a real user change should. Same idiom the
    // CheckBox Command binding uses elsewhere (a Command only fires on a real click); a plain
    // ComboBox has no Command, so the flag stands in for it.
    private bool _isInitialized;

    [ObservableProperty]
    private ObservableCollection<PlatformConfigItem> _platforms = new();

    [ObservableProperty]
    private PlatformConfigItem? _selectedPlatform;

    [ObservableProperty]
    private string _executablePath = string.Empty;

    [ObservableProperty]
    private string _argumentTemplate = string.Empty;

    [ObservableProperty]
    private string _steamGridDbApiKey = string.Empty;

    [ObservableProperty]
    private string _theGamesDbApiKey = string.Empty;

    // Mechanism 2 (ARCHITECTURE.md -> ADR-27) - default true, matching the approved design. Only
    // affects launches where a EmuBridge-managed cheat file already exists for that specific game
    // (LaunchService's own gate); this toggle just decides whether that already-narrow case also
    // gets auto-applied via RetroArch's own per-game override file, never a global RetroArch
    // behavior change.
    [ObservableProperty]
    private bool _autoApplyCheatsOnLaunch;

    // Phase Polish -> "Theme customization": System/Light/Dark, applied live on change.
    [ObservableProperty]
    private ThemePreference _themePreference = ThemePreference.System;

    // Phase Polish -> "Auto-updater": whether startup checks GitHub Releases for a newer build.
    [ObservableProperty]
    private bool _checkForUpdatesOnStartup = true;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public SettingsViewModel(
        ILibraryRepository libraryRepository,
        IEmulatorService emulatorService,
        IEmulatorInstallerService installerService,
        ISettingsService settingsService,
        IFilePickerService filePickerService,
        IMessageBoxService messageBoxService,
        IThemeService themeService,
        IUpdateService updateService)
    {
        _libraryRepository = libraryRepository;
        _emulatorService = emulatorService;
        _installerService = installerService;
        _settingsService = settingsService;
        _filePickerService = filePickerService;
        _messageBoxService = messageBoxService;
        _themeService = themeService;
        _updateService = updateService;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await LoadPlatformsAsync(ct);
        SteamGridDbApiKey = await _settingsService.GetSteamGridDbApiKeyAsync(ct) ?? string.Empty;
        TheGamesDbApiKey = await _settingsService.GetTheGamesDbApiKeyAsync(ct) ?? string.Empty;
        AutoApplyCheatsOnLaunch = await _settingsService.GetAutoApplyCheatsOnLaunchAsync(ct);
        ThemePreference = await _settingsService.GetThemePreferenceAsync(ct);
        CheckForUpdatesOnStartup = await _settingsService.GetCheckForUpdatesOnStartupAsync(ct);
        _isInitialized = true;
    }

    partial void OnThemePreferenceChanged(ThemePreference value)
    {
        if (!_isInitialized)
        {
            return;
        }

        _themeService.ApplyTheme(value);
        _ = _settingsService.SetThemePreferenceAsync(value);
    }

    partial void OnCheckForUpdatesOnStartupChanged(bool value)
    {
        if (!_isInitialized)
        {
            return;
        }

        _ = _settingsService.SetCheckForUpdatesOnStartupAsync(value);
    }

    partial void OnSelectedPlatformChanged(PlatformConfigItem? value)
    {
        ExecutablePath = value?.ExecutablePath ?? string.Empty;
        ArgumentTemplate = value?.ArgumentTemplate ?? string.Empty;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void BrowseExecutable()
    {
        var path = _filePickerService.PickFile("Select Emulator Executable", "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*");
        if (path is not null)
        {
            ExecutablePath = path;
        }
    }

    [RelayCommand]
    private async Task SaveEmulatorProfileAsync()
    {
        if (SelectedPlatform is null)
        {
            return;
        }

        var platformId = SelectedPlatform.PlatformId;

        try
        {
            await _emulatorService.SaveProfileAsync(
                platformId,
                $"{SelectedPlatform.PlatformName} Emulator",
                ExecutablePath,
                ArgumentTemplate);
        }
        catch (EmuBridgeException ex)
        {
            _messageBoxService.Show(ex.Message, "Couldn't Save Emulator Config", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Reselect before setting the final status message, not after — reassigning
        // SelectedPlatform fires OnSelectedPlatformChanged, which itself clears StatusMessage
        // (by design, for when the user manually switches platforms). Setting the message first
        // would just get wiped out by that side effect.
        await ReloadPlatformsAndReselectAsync(platformId);
        StatusMessage = "Saved.";
    }

    [RelayCommand]
    private async Task AutoInstallAsync()
    {
        if (SelectedPlatform is null || IsBusy)
        {
            return;
        }

        // Captured upfront, not read again after the awaits below — nothing stops the user from
        // changing the ListBox selection while a long install is in flight (only the Save/
        // Auto-Install buttons are IsBusy-guarded, not the platform list itself), and installing
        // against a since-changed SelectedPlatform would be a real, if narrow, correctness bug.
        var platformId = SelectedPlatform.PlatformId;
        var platformName = SelectedPlatform.PlatformName;

        var confirmed = _messageBoxService.Show(
            $"This will download and install an emulator for {platformName} automatically. It may take a while depending on your connection. Continue?",
            "Auto-Install Emulator",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        IsBusy = true;
        _installCts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<string>(message => StatusMessage = message);
            var result = await _installerService.InstallAsync(platformId, progress, _installCts.Token);

            if (result.Outcome != InstallOutcome.Success)
            {
                _messageBoxService.Show(
                    result.ErrorMessage ?? "Install failed.",
                    "Couldn't Auto-Install",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                StatusMessage = string.Empty;
                return;
            }

            // Same ordering reason as SaveEmulatorProfileAsync: reselect first, set the final
            // message after — OnSelectedPlatformChanged clears StatusMessage as a side effect.
            await ReloadPlatformsAndReselectAsync(platformId);
            StatusMessage = "Installed.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cancelled.";
        }
        finally
        {
            IsBusy = false;
            _installCts?.Dispose();
            _installCts = null;
        }
    }

    [RelayCommand]
    private void CancelInstall()
    {
        _installCts?.Cancel();
    }

    [RelayCommand]
    private async Task SaveApiKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(SteamGridDbApiKey))
        {
            return;
        }

        await _settingsService.SetSteamGridDbApiKeyAsync(SteamGridDbApiKey);
        StatusMessage = "API key saved.";
    }

    [RelayCommand]
    private async Task SaveTheGamesDbApiKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(TheGamesDbApiKey))
        {
            return;
        }

        await _settingsService.SetTheGamesDbApiKeyAsync(TheGamesDbApiKey);
        StatusMessage = "API key saved.";
    }

    // Bound to the CheckBox's Command (not an OnAutoApplyCheatsOnLaunchChanged partial hook) so
    // that InitializeAsync's own load of the persisted value never triggers a spurious write back
    // to disk - a Command only fires on a real user click, same idiom as CheatItem's
    // ToggleCheatCommand.
    [RelayCommand]
    private async Task SaveAutoApplyCheatsOnLaunchAsync()
    {
        await _settingsService.SetAutoApplyCheatsOnLaunchAsync(AutoApplyCheatsOnLaunch);
    }

    // Phase Polish -> "Auto-updater": the manual "Check for Updates" button. Interactive by
    // design (the user asked for this check), unlike the silent startup check — an available
    // update is offered directly, and confirmation runs the download+swap which restarts the app.
    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            var update = await _updateService.CheckForUpdateAsync();
            if (!update.IsUpdateAvailable)
            {
                _messageBoxService.Show(
                    $"You're up to date — EmuBridge {update.CurrentVersionText ?? "?"} is the latest version.",
                    "No Update Available",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var confirmed = _messageBoxService.Show(
                $"EmuBridge {update.CurrentVersionText} is installed, and {update.LatestVersionText} is available.\n\n" +
                "Download and install it now? EmuBridge will restart automatically.",
                "Update Available",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmed != MessageBoxResult.Yes)
            {
                return;
            }

            StatusMessage = "Downloading update...";
            var progress = new Progress<string>(message => StatusMessage = message);
            var result = await _updateService.DownloadAndApplyAsync(update, progress);

            if (result.Outcome != UpdateApplyOutcome.Success)
            {
                _messageBoxService.Show(
                    result.ErrorMessage ?? "The update could not be applied.",
                    "Update Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            StatusMessage = string.Empty;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadPlatformsAsync(CancellationToken ct = default)
    {
        var platforms = await _libraryRepository.GetPlatformsAsync(ct);

        // Excludes the "unknown" sentinel — configuring an emulator for "couldn't identify this
        // ROM's system" doesn't make sense; the fix there is the extension mapping, not an
        // emulator assignment. Platform count is small (~15 seeded), so an N-query loop here
        // isn't the same concern as the Games/BoxArt bulk-load in MainViewModel.
        var items = new List<PlatformConfigItem>();
        foreach (var platform in platforms.Where(p => p.Id != Config.UnknownPlatformId))
        {
            var profile = await _emulatorService.GetProfileForPlatformAsync(platform.Id, ct);
            var hasKnownInstallOption = await _installerService.HasKnownInstallOptionAsync(platform.Id, ct);
            items.Add(new PlatformConfigItem
            {
                PlatformId = platform.Id,
                PlatformName = platform.Name,
                IsConfigured = profile is not null,
                ExecutablePath = profile?.ExecutablePath,
                ArgumentTemplate = profile?.ArgumentTemplate,
                HasKnownInstallOption = hasKnownInstallOption
            });
        }

        Platforms = new ObservableCollection<PlatformConfigItem>(items.OrderBy(p => p.PlatformName));
    }

    // LoadPlatformsAsync rebuilds Platforms with brand-new PlatformConfigItem instances every
    // call — SelectedPlatform, left pointing at the old (now orphaned) instance, would otherwise
    // keep showing pre-reload data (ExecutablePath/ArgumentTemplate only refresh inside
    // OnSelectedPlatformChanged, which only fires on a real reassignment). Explicitly reselecting
    // the matching item from the fresh collection is what actually triggers that refresh.
    private async Task ReloadPlatformsAndReselectAsync(string platformId, CancellationToken ct = default)
    {
        await LoadPlatformsAsync(ct);
        SelectedPlatform = Platforms.FirstOrDefault(p => p.PlatformId == platformId);
    }
}
