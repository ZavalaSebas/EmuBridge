using Bridge.Services;

namespace Bridge.Tests.Services;

internal class FakeSettingsService : ISettingsService
{
    public string? ApiKey { get; set; }

    public Task<string?> GetSteamGridDbApiKeyAsync(CancellationToken ct = default)
        => Task.FromResult(ApiKey);

    public Task SetSteamGridDbApiKeyAsync(string apiKey, CancellationToken ct = default)
    {
        ApiKey = apiKey;
        return Task.CompletedTask;
    }
}
