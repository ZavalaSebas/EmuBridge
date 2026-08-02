using System.Windows;
using Bridge.Exceptions;
using Bridge.Models;
using Bridge.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bridge.ViewModels;

public partial class EmulatorOverrideViewModel : ObservableObject
{
    private readonly IEmulatorService _emulatorService;
    private readonly ILibraryRepository _libraryRepository;
    private readonly IFilePickerService _filePickerService;
    private readonly IMessageBoxService _messageBoxService;
    private Game? _game;

    [ObservableProperty]
    private string _gameName = string.Empty;

    [ObservableProperty]
    private string _platformName = string.Empty;

    [ObservableProperty]
    private string _executablePath = string.Empty;

    [ObservableProperty]
    private string _argumentTemplate = string.Empty;

    // Whether a per-game override currently exists — distinguishes "editing an existing override"
    // from "the fields below are just the platform default, shown as a starting point" (ARCHITECTURE.md
    // -> ADR-24). Drives whether "Remove Override" is enabled and the status text shown.
    [ObservableProperty]
    private bool _hasOverride;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public EmulatorOverrideViewModel(
        IEmulatorService emulatorService,
        ILibraryRepository libraryRepository,
        IFilePickerService filePickerService,
        IMessageBoxService messageBoxService)
    {
        _emulatorService = emulatorService;
        _libraryRepository = libraryRepository;
        _filePickerService = filePickerService;
        _messageBoxService = messageBoxService;
    }

    /// <summary>Set synchronously before the window shows, so the title/name render immediately —
    /// InitializeAsync then fills in the parts that need a repository round-trip.</summary>
    public void SetGame(Game game)
    {
        _game = game;
        GameName = game.Name;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_game is null)
        {
            return;
        }

        var platforms = await _libraryRepository.GetPlatformsAsync(ct);
        PlatformName = platforms.FirstOrDefault(p => p.Id == _game.PlatformId)?.Name ?? _game.PlatformId;

        HasOverride = await _emulatorService.HasGameOverrideAsync(_game.Id, ct);

        // GetProfileForGameAsync already falls back to the platform default when there's no
        // override, so the fields start pre-filled either way — never blank for a platform that's
        // actually configured (ARCHITECTURE.md -> ADR-24).
        var profile = await _emulatorService.GetProfileForGameAsync(_game, ct);
        ExecutablePath = profile?.ExecutablePath ?? string.Empty;
        ArgumentTemplate = profile?.ArgumentTemplate ?? string.Empty;
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
    private async Task SaveAsync()
    {
        if (_game is null)
        {
            return;
        }

        try
        {
            await _emulatorService.SaveProfileAsync(
                _game.PlatformId,
                $"{_game.Name} Override",
                ExecutablePath,
                ArgumentTemplate,
                _game.Id);
        }
        catch (BridgeException ex)
        {
            _messageBoxService.Show(ex.Message, "Couldn't Save Override", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        HasOverride = true;
        StatusMessage = "Saved.";
    }

    [RelayCommand]
    private async Task ClearOverrideAsync()
    {
        if (_game is null || !HasOverride)
        {
            return;
        }

        var confirmed = _messageBoxService.Show(
            $"Remove the emulator override for \"{_game.Name}\"? It will go back to using {PlatformName}'s default emulator.",
            "Remove Override?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        await _emulatorService.ClearGameOverrideAsync(_game.Id);
        HasOverride = false;

        // Reload so the fields fall back to showing the platform default, not the just-removed
        // override's stale values.
        var profile = await _emulatorService.GetProfileForPlatformAsync(_game.PlatformId);
        ExecutablePath = profile?.ExecutablePath ?? string.Empty;
        ArgumentTemplate = profile?.ArgumentTemplate ?? string.Empty;
        StatusMessage = "Override removed.";
    }
}
