using System.Net.Http;
using System.Windows;
using Bridge.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bridge;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        // Built here, in the constructor, rather than in OnStartup like SteamManager's
        // App.xaml.cs — Bridge still uses the default StartupUri="MainWindow.xaml" mechanism
        // (no real MainViewModel to wire up yet), so this just needs Services to be ready
        // before that default window-creation path runs. Once a real MainViewModel/MainWindow
        // wiring step replaces StartupUri, this can move into OnStartup the same way.
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();
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
        // LaunchService: Singleton here is a simplicity choice, not a technical requirement —
        // documented deviation from DEVELOPMENT.md's own "Transient: lightweight stateless
        // services" guideline, not an oversight. None of the six hold mutable instance state
        // (checked field-by-field): every method builds its working data as local variables
        // per call, not instance fields. Transient would be equally correct — there's no state
        // to isolate between resolutions either way, so the choice costs nothing functionally,
        // just a handful of avoided allocations. Secondary, non-driving reason: if one of these
        // ever gains real state later (e.g. LaunchService tracking "is this game already
        // running", not built today), it's already registered the way that would need.
        services.AddSingleton<IRomScannerService, RomScannerService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IImageCacheService, ImageCacheService>();
        services.AddSingleton<IMetadataService, MetadataService>();
        services.AddSingleton<IEmulatorService, EmulatorService>();
        services.AddSingleton<ILaunchService, LaunchService>();

        // No factory lambdas needed for any of the above — every constructor dependency
        // (ILogger<T>, HttpClient, and the other service interfaces) is itself a directly
        // registered type, so the container resolves them automatically. This differs from
        // SteamManager's ISmartUnlockService, which needed a factory lambda
        // (sp => new SmartUnlockService(sp.GetRequiredService<SteamContext>().Achievements, ...))
        // because it depended on properties of a registered service (SteamContext.Achievements/
        // .Stats), not on registered types themselves. If a future Bridge service ends up in that
        // same shape, register it the same way.

        // ViewModels: none exist yet. The first one added here should use AddTransient,
        // matching SteamManager's MainViewModel/GamePickerViewModel/GameManagerViewModel pattern.
    }

    protected override void OnExit(ExitEventArgs e)
    {
        (Services as IDisposable)?.Dispose();
        base.OnExit(e);
    }
}
