using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Bridge.Models;
using Bridge.Services;
using Bridge.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bridge;

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
    // threads, unobserved fire-and-forget Tasks) aren't wired up: nothing in Bridge today runs
    // meaningful work off the dispatcher thread, and the one fire-and-forget call that exists
    // (MainViewModel.TrackSessionEndAsync) already catches everything internally, so there's no
    // real gap there to close right now — not deferred, just not a currently-live risk.
    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var logger = Services.GetService<ILogger<App>>();
        logger?.LogError(e.Exception, "Unhandled exception on the UI thread.");

        var result = MessageBox.Show(
            $"Bridge ran into an unexpected error:\n\n{e.Exception.Message}\n\nTry to continue anyway? Choosing No will close Bridge.",
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

        // Fire-and-forget, deliberately not awaited: never delays showing the library, and by
        // the time a user actually clicks "Auto-Install" (a later, human-initiated action, never
        // immediate at startup) this has almost always already finished. See ARCHITECTURE.md ->
        // ADR-25 and IManifestUpdateService's own doc comment for why a failed/slow refresh here
        // is silent rather than surfaced.
        _ = Services.GetRequiredService<IManifestUpdateService>().RefreshAsync();

        var mainWindow = new MainWindow();
        var mainViewModel = Services.GetRequiredService<MainViewModel>();
        mainViewModel.OpenSettingsRequested = () => OpenSettings(mainWindow);
        mainViewModel.OpenGameDetailsRequested = game => OpenGameDetails(mainWindow, game);
        mainViewModel.OpenEmulatorOverrideRequested = game => OpenEmulatorOverride(mainWindow, game);
        mainWindow.DataContext = mainViewModel;

        MainWindow = mainWindow;
        mainWindow.Show();
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
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IImageCacheService, ImageCacheService>();
        services.AddSingleton<IMetadataService, MetadataService>();
        services.AddSingleton<IEmulatorService, EmulatorService>();
        services.AddSingleton<ILaunchService, LaunchService>();
        services.AddSingleton<IDownloadVerificationService, DownloadVerificationService>();
        services.AddSingleton<IManifestUpdateService, ManifestUpdateService>();
        services.AddSingleton<IEmulatorInstallerService, EmulatorInstallerService>();
        services.AddSingleton<IMessageBoxService, MessageBoxService>();
        services.AddSingleton<IFolderPickerService, FolderPickerService>();
        services.AddSingleton<IFilePickerService, FilePickerService>();

        // No factory lambdas needed for any of the above — every constructor dependency
        // (ILogger<T>, HttpClient, and the other service interfaces) is itself a directly
        // registered type, so the container resolves them automatically. This differs from
        // SteamManager's ISmartUnlockService, which needed a factory lambda
        // (sp => new SmartUnlockService(sp.GetRequiredService<SteamContext>().Achievements, ...))
        // because it depended on properties of a registered service (SteamContext.Achievements/
        // .Stats), not on registered types themselves. If a future Bridge service ends up in that
        // same shape, register it the same way.

        services.AddTransient<MainViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<GameDetailViewModel>();
        services.AddTransient<EmulatorOverrideViewModel>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        (Services as IDisposable)?.Dispose();
        base.OnExit(e);
    }
}
