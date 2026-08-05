using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using EmuBridge.Models;
using EmuBridge.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace EmuBridge.ViewModels;

public partial class CheatsViewModel : ObservableObject
{
    // Generic project credit, shown whenever cheats are displayed - the per-file link
    // (SourceFileUrl) is the real Section 3.a "link to the licensed material" requirement, this is
    // the fallback identification-of-the-source when no per-file link is available (a local .cht
    // that predates this field, or was placed by hand). See ARCHITECTURE.md -> ADR-27.
    public const string ProjectAttributionUrl = "https://github.com/libretro/libretro-database";

    private readonly ICheatService _cheatService;
    private readonly IEmulatorService _emulatorService;
    private readonly ILibraryRepository _libraryRepository;
    private readonly IMessageBoxService _messageBoxService;
    private Game? _game;

    [ObservableProperty]
    private string _gameName = string.Empty;

    [ObservableProperty]
    private string _platformName = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    // Explanation text for every non-success state (not available, platform not supported, not
    // found, fetch failed, corrupted) - never left blank when Cheats is empty, matching ADR-19's
    // "Description: not available" standard of always saying so explicitly rather than showing
    // nothing.
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasCheats;

    [ObservableProperty]
    private ObservableCollection<CheatItem> _cheats = [];

    [ObservableProperty]
    private string? _sourceFileUrl;

    public CheatsViewModel(
        ICheatService cheatService,
        IEmulatorService emulatorService,
        ILibraryRepository libraryRepository,
        IMessageBoxService messageBoxService)
    {
        _cheatService = cheatService;
        _emulatorService = emulatorService;
        _libraryRepository = libraryRepository;
        _messageBoxService = messageBoxService;
    }

    /// <summary>Set synchronously before the window shows, so the title/name render immediately —
    /// same shape as EmulatorOverrideViewModel.SetGame.</summary>
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

        IsLoading = true;
        try
        {
            var platforms = await _libraryRepository.GetPlatformsAsync(ct);
            PlatformName = platforms.FirstOrDefault(p => p.Id == _game.PlatformId)?.Name ?? _game.PlatformId;

            // Cheats only make sense for a RetroArch-backed profile (CorePath set) - a manually
            // configured standalone emulator has no cheat mechanism EmuBridge understands. No
            // visibility gate on the context-menu item itself (MainWindow.xaml matches
            // "Configure Emulator..."'s existing precedent of no per-tile gating); this state is
            // the honest, explicit alternative to a dead-end menu item.
            var profile = await _emulatorService.GetProfileForGameAsync(_game, ct);
            if (profile?.CorePath is null)
            {
                StatusMessage = "Cheats require a RetroArch-installed emulator. This game's emulator is configured manually.";
                return;
            }

            var result = await _cheatService.LoadCheatsAsync(_game, _game.PlatformId, ct);
            ApplyResult(result);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyResult(CheatsResult result)
    {
        SourceFileUrl = result.SourceFileUrl;

        switch (result.Outcome)
        {
            case CheatFetchOutcome.Success when result.Cheats.Count == 0:
            case CheatFetchOutcome.NotFound:
                HasCheats = false;
                StatusMessage = "No cheats found for this game.";
                break;

            case CheatFetchOutcome.Success:
                HasCheats = true;
                StatusMessage = string.Empty;
                Cheats = new ObservableCollection<CheatItem>(
                    result.Cheats.Select(c => new CheatItem(c.Index, c.Description, c.Enabled)));
                break;

            case CheatFetchOutcome.PlatformNotSupported:
                HasCheats = false;
                StatusMessage = "This platform isn't in the public cheat database EmuBridge uses.";
                break;

            case CheatFetchOutcome.FetchFailed:
                HasCheats = false;
                StatusMessage = result.ErrorMessage ?? "Couldn't fetch cheats for this game.";
                break;

            case CheatFetchOutcome.Corrupted:
                HasCheats = false;
                StatusMessage = result.ErrorMessage ?? "This game's cheat file couldn't be read.";
                break;
        }
    }

    [RelayCommand]
    private async Task ToggleCheatAsync(CheatItem? item)
    {
        if (_game is null || item is null)
        {
            return;
        }

        try
        {
            await _cheatService.SetCheatEnabledAsync(_game, item.Index, item.Enabled);
        }
        catch (IOException ex)
        {
            // Revert the visual toggle - the write didn't actually land, so the checkbox must not
            // silently imply it did (never-fail-silently, same standard as everywhere else).
            item.Enabled = !item.Enabled;
            _messageBoxService.Show($"Couldn't save this cheat's state: {ex.Message}", "Couldn't Save Cheat", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
