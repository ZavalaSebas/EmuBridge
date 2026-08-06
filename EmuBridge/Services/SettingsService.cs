using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EmuBridge.Models;
using Microsoft.Extensions.Logging;

namespace EmuBridge.Services;

public class SettingsService : ISettingsService
{
    private readonly string _settingsPath;
    private readonly ILogger<SettingsService> _logger;

    public SettingsService(ILogger<SettingsService> logger)
        : this(Config.SettingsPath, logger)
    {
    }

    public SettingsService(string settingsPath, ILogger<SettingsService> logger)
    {
        _settingsPath = settingsPath;
        _logger = logger;
    }

    public async Task<string?> GetSteamGridDbApiKeyAsync(CancellationToken ct = default)
    {
        var settings = await ReadSettingsAsync(ct);
        return DecryptOrNull(settings?.EncryptedSteamGridDbApiKey, "SteamGridDB");
    }

    public async Task SetSteamGridDbApiKeyAsync(string apiKey, CancellationToken ct = default)
    {
        var settings = await ReadSettingsAsync(ct) ?? new SettingsFile();
        settings.EncryptedSteamGridDbApiKey = Encrypt(apiKey);

        await WriteSettingsAsync(settings, ct);
    }

    public async Task<string?> GetTheGamesDbApiKeyAsync(CancellationToken ct = default)
    {
        var settings = await ReadSettingsAsync(ct);
        return DecryptOrNull(settings?.EncryptedTheGamesDbApiKey, "TheGamesDB");
    }

    public async Task SetTheGamesDbApiKeyAsync(string apiKey, CancellationToken ct = default)
    {
        var settings = await ReadSettingsAsync(ct) ?? new SettingsFile();
        settings.EncryptedTheGamesDbApiKey = Encrypt(apiKey);

        await WriteSettingsAsync(settings, ct);
    }

    private static string Encrypt(string plainText)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encryptedBytes = ProtectedData.Protect(plainBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encryptedBytes);
    }

    private string? DecryptOrNull(string? encrypted, string keyName)
    {
        if (string.IsNullOrEmpty(encrypted))
        {
            return null;
        }

        try
        {
            var encryptedBytes = Convert.FromBase64String(encrypted);
            var decryptedBytes = ProtectedData.Unprotect(encryptedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decryptedBytes);
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(
                ex,
                "Could not decrypt the stored {KeyName} API key — it may have been created under a different Windows account. Treating as not configured.",
                keyName);
            return null;
        }
        catch (FormatException ex)
        {
            _logger.LogWarning(ex, "Stored {KeyName} API key is not valid base64; treating as not configured.", keyName);
            return null;
        }
    }

    public async Task<bool> GetAutoApplyCheatsOnLaunchAsync(CancellationToken ct = default)
    {
        var settings = await ReadSettingsAsync(ct);
        // Nullable so "never set" (null) is distinguishable from an explicit false - defaults to
        // true either way (approved design), but keeps the file's own on-disk state honest about
        // whether the user ever actually touched this toggle.
        return settings?.AutoApplyCheatsOnLaunch ?? true;
    }

    public async Task SetAutoApplyCheatsOnLaunchAsync(bool enabled, CancellationToken ct = default)
    {
        var settings = await ReadSettingsAsync(ct) ?? new SettingsFile();
        settings.AutoApplyCheatsOnLaunch = enabled;

        await WriteSettingsAsync(settings, ct);
    }

    public async Task<ThemePreference> GetThemePreferenceAsync(CancellationToken ct = default)
    {
        var settings = await ReadSettingsAsync(ct);
        // Nullable so "never set" is distinguishable from an explicit System — both resolve to
        // System (follow-the-Windows-theme, the WPF-UI integration's own default, ADR-29), but the
        // file stays honest about whether the user ever chose a theme at all.
        return settings?.Theme ?? ThemePreference.System;
    }

    public async Task SetThemePreferenceAsync(ThemePreference preference, CancellationToken ct = default)
    {
        var settings = await ReadSettingsAsync(ct) ?? new SettingsFile();
        settings.Theme = preference;

        await WriteSettingsAsync(settings, ct);
    }

    public async Task<bool> GetCheckForUpdatesOnStartupAsync(CancellationToken ct = default)
    {
        var settings = await ReadSettingsAsync(ct);
        // Nullable so "never set" (null) is distinguishable from an explicit false — defaults to
        // true either way, matching the approved "check on startup by default" design.
        return settings?.CheckForUpdatesOnStartup ?? true;
    }

    public async Task SetCheckForUpdatesOnStartupAsync(bool enabled, CancellationToken ct = default)
    {
        var settings = await ReadSettingsAsync(ct) ?? new SettingsFile();
        settings.CheckForUpdatesOnStartup = enabled;

        await WriteSettingsAsync(settings, ct);
    }

    private async Task<SettingsFile?> ReadSettingsAsync(CancellationToken ct)
    {
        if (!File.Exists(_settingsPath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(_settingsPath);
            return await JsonSerializer.DeserializeAsync<SettingsFile>(stream, cancellationToken: ct);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse {SettingsPath}; treating settings as empty.", _settingsPath);
            return null;
        }
    }

    private async Task WriteSettingsAsync(SettingsFile settings, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_settingsPath);
        await JsonSerializer.SerializeAsync(stream, settings, cancellationToken: ct);
    }

    // internal (not private) so ThemeService can reuse it for its synchronous startup read —
    // settings.json has exactly one writer (this class); ThemeService only ever reads, so sharing
    // the model guarantees both sides agree on the shape and nothing is ever clobbered.
    internal class SettingsFile
    {
        public string? EncryptedSteamGridDbApiKey { get; set; }
        public string? EncryptedTheGamesDbApiKey { get; set; }
        public bool? AutoApplyCheatsOnLaunch { get; set; }

        // Serialized by name, not number, so reordering the enum never corrupts an existing
        // settings.json (the field is user-editable text; a raw int would be opaque).
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ThemePreference? Theme { get; set; }

        public bool? CheckForUpdatesOnStartup { get; set; }
    }
}
