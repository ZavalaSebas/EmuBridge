using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using EmuBridge.Models;
using EmuBridge.Services;
using EmuBridge.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EmuBridge;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        DispatcherUnhandledException += App_DispatcherUnhandledException;
    }

    // DEVELOPMENT.md -> Error Handling requires this. Covers the UI (dispatcher) thread, where
    // exceptions from ViewModel commands and data binding actually surface — the common case for
    // a WPF app. AppDomain.UnhandledException / TaskScheduler.UnobservedTaskException (background
    // threads, unobserved fire-and-forget Tasks) aren't wired up: nothing in EmuBridge today runs
    // meaningful work off the dispatcher thread, and the one fire-and-forget call that exists
    // (MainViewModel.TrackSessionEndAsync) already catches everything internally, so there's no
    // real gap there to close right now — not deferred, just not a currently-live risk.
    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var logger = Services.GetService<ILogger<App>>();
        logger?.LogError(e.Exception, "Unhandled exception on the UI thread.");

        var result = MessageBox.Show(
            $"EmuBridge ran into an unexpected error:\n\n{e.Exception.Message}\n\nTry to continue anyway? Choosing No will close EmuBridge.",
            "Unexpected Error",
            MessageBoxButton.YesNo,
            MessageBoxImage.Error);

        // Handled = true either way: we've already shown our own message and given the user a
        // choice, so we don't also want WPF's default unhandled-exception crash behavior on top.
        // For "No", explicitly Shutdown() rather than leaving the process to terminate on its
        // own — that's what actually runs OnExit and disposes Services cleanly.
        e.Handled = true;

        if (result == MessageBoxResult.No)
        {
            Shutdown();
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Must run before anything else touches AppDataPath (SettingsService, LibraryRepository,
        // etc.) - moves an existing install's real data from the legacy %LOCALAPPDATA%\EmuBridge
        // folder to the renamed one. Synchronous and first, not fire-and-forget: every other
        // startup step depends on the data already being at the new path by the time it runs.
        Services.GetRequiredService<IAppDataMigrationService>().MigrateIfNeeded();

        // Applies the user's persisted theme before the first window is created, so the first
        // frame already shows it instead of a flash of the system default. Overrides App.xaml's
        // Theme="Dark" fallback; System (follow Windows) is the default unless the user picked
        // Light/Dark in Settings (Phase Polish -> "Theme customization").
        Services.GetRequiredService<IThemeService>().ApplyPersistedTheme();

        // Fire-and-forget, deliberately not awaited: never delays showing the library, and by
        // the time a user actually clicks "Auto-Install" (a later, human-initiated action, never
        // immediate at startup) this has almost always already finished. See ARCHITECTURE.md ->
        // ADR-25 and IManifestUpdateService's own doc comment for why a failed/slow refresh here
        // is silent rather than surfaced.
        _ = Services.GetRequiredService<IManifestUpdateService>().RefreshAsync();

        // A running process is the signal the previous update's new version started fine, so any
        // leftover current-exe.old from that swap is now safe to delete (UpdateService).
        Services.GetRequiredService<IUpdateService>().CleanupOldExecutable();

        var mainWindow = new MainWindow();
        var mainViewModel = Services.GetRequiredService<MainViewModel>();
        mainViewModel.OpenSettingsRequested = () => OpenSettings(mainWindow);
        mainViewModel.OpenGameDetailsRequested = game => OpenGameDetails(mainWindow, game);
        mainViewModel.OpenEmulatorOverrideRequested = game => OpenEmulatorOverride(mainWindow, game);
        mainViewModel.OpenCheatsRequested = game => OpenCheats(mainWindow, game);
        mainViewModel.OpenAboutRequested = () => OpenAbout(mainWindow);
        mainWindow.DataContext = mainViewModel;

        MainWindow = mainWindow;
        mainWindow.Show();

        ShowWelcomeIfNewVersion(mainWindow);

        // Fire-and-forget update check (respects the Settings toggle): non-blocking, never
        // surfaces errors, and any dialog it shows is marshalled back onto the UI thread because
        // the awaits below capture the dispatcher SynchronizationContext at this call site.
        _ = CheckForUpdatesAsync(mainWindow);
    }

    private void ShowWelcomeIfNewVersion(Window owner)
    {
        var sentinel = Services.GetRequiredService<IWelcomeSentinelService>();
        if (!sentinel.ShouldShowWelcome())
        {
            return;
        }

        var welcome = new WelcomeWindow { Owner = owner };
        welcome.ShowDialog();
        sentinel.MarkWelcomeShown();
    }

    // The auto-updater's startup path (Phase Polish -> "Auto-updater"): silent when there's
    // nothing newer or the check itself fails, and interactive only when an update is genuinely
    // available — offering to download/apply it then and there. The actual swap restarts the app,
    // so everything here is fire-and-forget from OnStartup's perspective.
    private async Task CheckForUpdatesAsync(Window owner)
    {
        try
        {
            var settings = Services.GetRequiredService<ISettingsService>();
            if (!await settings.GetCheckForUpdatesOnStartupAsync())
            {
                return;
            }

            var updateService = Services.GetRequiredService<IUpdateService>();
            var update = await updateService.CheckForUpdateAsync();
            if (!update.IsUpdateAvailable)
            {
                return;
            }

            var result = MessageBox.Show(
                $"EmuBridge {update.CurrentVersionText} is installed, and {update.LatestVersionText} is available.\n\n" +
                "Download and install it now? EmuBridge will restart automatically.",
                "Update Available",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            var progress = new Progress<string>(message => owner.Title = $"EmuBridge — {message}");
            await updateService.DownloadAndApplyAsync(update, progress);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never let a background update check crash or nag — it's an offer, not a requirement.
            Services.GetService<ILogger<App>>()?.LogWarning(ex, "Background update check failed.");
        }
    }

    private void OpenSettings(Window owner)
    {
        var settingsWindow = new SettingsWindow
        {
            Owner = owner,
            DataContext = Services.GetRequiredService<SettingsViewModel>()
        };
        settingsWindow.ShowDialog();
    }

    private void OpenGameDetails(Window owner, Game game)
    {
        var viewModel = Services.GetRequiredService<GameDetailViewModel>();
        viewModel.SetGame(game);
        var detailWindow = new GameDetailWindow
        {
            Owner = owner,
            DataContext = viewModel
        };
        detailWindow.ShowDialog();
    }

    private void OpenEmulatorOverride(Window owner, Game game)
    {
        var viewModel = Services.GetRequiredService<EmulatorOverrideViewModel>();
        viewModel.SetGame(game);
        var overrideWindow = new EmulatorOverrideWindow
        {
            Owner = owner,
            DataContext = viewModel
        };
        overrideWindow.ShowDialog();
    }

    private void OpenCheats(Window owner, Game game)
    {
        var viewModel = Services.GetRequiredService<CheatsViewModel>();
        viewModel.SetGame(game);
        var cheatsWindow = new CheatsWindow
        {
            Owner = owner,
            DataContext = viewModel
        };
        cheatsWindow.ShowDialog();
    }

    private void OpenAbout(Window owner)
    {
        var aboutWindow = new AboutWindow { Owner = owner };
        aboutWindow.ShowDialog();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Shared by MetadataService (SteamGridDB calls) and ImageCacheService (image downloads).
        // A single long-lived HttpClient, not one per request — standard guidance to avoid
        // socket exhaustion. Neither service sets BaseAddress, so sharing one instance is safe.
        services.AddSingleton<HttpClient>();

        // LibraryRepository: Singleton is required, not just chosen — it owns the one LiteDatabase
        // connection for the app's lifetime. Real state, real justification.
        services.AddSingleton<ILibraryRepository, LibraryRepository>();

        // RomScannerService/SettingsService/ImageCacheService/MetadataService/EmulatorService/
        // LaunchService/MessageBoxService/FolderPickerService/FilePickerService: Singleton here
        // is a simplicity choice, not a technical requirement — documented deviation from
        // DEVELOPMENT.md's own "Transient: lightweight stateless services" guideline, not an
        // oversight. None hold mutable instance state (checked field-by-field for the original
        // six; the three dialog wrappers below have no fields at all). Transient would be
        // equally correct — there's no state to isolate between resolutions either way, so the
        // choice costs nothing functionally, just a handful of avoided allocations.
        services.AddSingleton<IRomScannerService, RomScannerService>();
        services.AddSingleton<IAppDataMigrationService, AppDataMigrationService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IImageCacheService, ImageCacheService>();
        services.AddSingleton<IMetadataService, MetadataService>();
        services.AddSingleton<ITheGamesDbService, TheGamesDbService>();
        services.AddSingleton<IEmulatorService, EmulatorService>();
        services.AddSingleton<ILaunchService, LaunchService>();
        services.AddSingleton<IDownloadVerificationService, DownloadVerificationService>();
        services.AddSingleton<IManifestUpdateService, ManifestUpdateService>();
        services.AddSingleton<IEmulatorInstallerService, EmulatorInstallerService>();
        services.AddSingleton<ICheatService, CheatService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IWelcomeSentinelService, WelcomeSentinelService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IMessageBoxService, MessageBoxService>();
        services.AddSingleton<IFolderPickerService, FolderPickerService>();
        services.AddSingleton<IFilePickerService, FilePickerService>();

        // No factory lambdas needed for any of the above — every constructor dependency
        // (ILogger<T>, HttpClient, and the other service interfaces) is itself a directly
        // registered type, so the container resolves them automatically. This differs from
        // SteamManager's ISmartUnlockService, which needed a factory lambda
        // (sp => new SmartUnlockService(sp.GetRequiredService<SteamContext>().Achievements, ...))
        // because it depended on properties of a registered service (SteamContext.Achievements/
        // .Stats), not on registered types themselves. If a future EmuBridge service ends up in that
        // same shape, register it the same way.

        services.AddTransient<MainViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<GameDetailViewModel>();
        services.AddTransient<EmulatorOverrideViewModel>();
        services.AddTransient<CheatsViewModel>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        (Services as IDisposable)?.Dispose();
        base.OnExit(e);
    }
}
