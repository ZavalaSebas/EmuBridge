namespace Bridge.Services;

public interface ISettingsService
{
    Task<string?> GetSteamGridDbApiKeyAsync(CancellationToken ct = default);
    Task SetSteamGridDbApiKeyAsync(string apiKey, CancellationToken ct = default);
}
