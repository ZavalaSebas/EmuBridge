namespace EmuBridge.Services;

public interface IAppDataMigrationService
{
    /// <summary>
    /// One-time, idempotent migration from the legacy %LOCALAPPDATA%\Bridge folder (from before
    /// the EmuBridge rename) to Config.AppDataPath. No-op if there's nothing to migrate. Must run
    /// before any other service touches AppDataPath.
    /// </summary>
    void MigrateIfNeeded();
}
