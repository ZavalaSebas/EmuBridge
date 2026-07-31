using System.Collections.ObjectModel;
using System.Windows;
using Bridge.Exceptions;
using Bridge.Models;
using Bridge.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bridge.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ILibraryRepository _libraryRepository;
    private readonly IEmulatorService _emulatorService;
    private readonly ISettingsService _settingsService;
    private readonly IFilePickerService _filePickerService;
    private readonly IMessageBoxService _messageBoxService;

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
    private string _statusMessage = string.Empty;

    public SettingsViewModel(
        ILibraryRepository libraryRepository,
        IEmulatorService emulatorService,
        ISettingsService settingsService,
        IFilePickerService filePickerService,
        IMessageBoxService messageBoxService)
    {
        _libraryRepository = libraryRepository;
        _emulatorService = emulatorService;
        _settingsService = settingsService;
        _filePickerService = filePickerService;
        _messageBoxService = messageBoxService;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await LoadPlatformsAsync(ct);
        SteamGridDbApiKey = await _settingsService.GetSteamGridDbApiKeyAsync(ct) ?? string.Empty;
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

        try
        {
            await _emulatorService.SaveProfileAsync(
                SelectedPlatform.PlatformId,
                $"{SelectedPlatform.PlatformName} Emulator",
                ExecutablePath,
                ArgumentTemplate);
        }
        catch (BridgeException ex)
        {
            _messageBoxService.Show(ex.Message, "Couldn't Save Emulator Config", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StatusMessage = "Saved.";
        await LoadPlatformsAsync();
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
            items.Add(new PlatformConfigItem
            {
                PlatformId = platform.Id,
                PlatformName = platform.Name,
                IsConfigured = profile is not null,
                ExecutablePath = profile?.ExecutablePath,
                ArgumentTemplate = profile?.ArgumentTemplate
            });
        }

        Platforms = new ObservableCollection<PlatformConfigItem>(items.OrderBy(p => p.PlatformName));
    }
}
