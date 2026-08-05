using System.IO;
using EmuBridge.Models;
using LiteDB;
using Microsoft.Extensions.Logging;

namespace EmuBridge.Services;

public class AppDataMigrationService : IAppDataMigrationService
{
    private readonly string _oldPath;
    private readonly string _newPath;
    private readonly ILogger<AppDataMigrationService> _logger;

    public AppDataMigrationService(ILogger<AppDataMigrationService> logger)
        : this(LegacyAppDataPath, Config.AppDataPath, logger)
    {
    }

    public AppDataMigrationService(string oldPath, string newPath, ILogger<AppDataMigrationService> logger)
    {
        _oldPath = oldPath;
        _newPath = newPath;
        _logger = logger;
    }

    // The pre-rename app data folder name, hardcoded here deliberately rather than reused from
    // any constant — this is the one place in the codebase that's supposed to still say "Bridge"
    // after the EmuBridge rename, since it has to keep pointing at where existing installs'
    // real data actually lives. internal (not private), with a real test asserting its exact
    // value directly — a blind repo-wide "Bridge" -> "EmuBridge" text replace silently broke this
    // exact line once already (rewrote it to "EmuBridge", pointing the "legacy" path at the new
    // folder instead of the old one) and every existing test still passed, because they all use
    // the 2-arg constructor with explicit paths and never exercise this property at all.
    internal static string LegacyAppDataPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Bridge");

    public void MigrateIfNeeded()
    {
        // Deliberately doesn't merge if both somehow exist; that's an ambiguous case a silent
        // move could get wrong (e.g. clobbering newer data with older), so it's left alone rather
        // than guessed at.
        if (!Directory.Exists(_newPath))
        {
            if (!Directory.Exists(_oldPath))
            {
                return; // fresh install, nothing to migrate
            }

            Directory.Move(_oldPath, _newPath);

            var oldDbPath = Path.Combine(_newPath, "bridge.db");
            var newDbPath = Path.Combine(_newPath, "emubridge.db");
            if (File.Exists(oldDbPath))
            {
                File.Move(oldDbPath, newDbPath);
            }

            _logger.LogInformation(
                "Migrated app data from the legacy Bridge folder ({OldPath}) to {NewPath}.",
                _oldPath,
                _newPath);
        }

        // Always attempted, even when the folder move above didn't run this time (e.g. an earlier
        // run of this migration - before this path-rewrite step existed - already moved the
        // folder, leaving stale paths behind with no future run ever revisiting them otherwise).
        // Idempotent either way: RewritePathPrefix only touches strings that still carry the old
        // prefix, so re-running against already-correct paths is a no-op, not a double-rewrite.
        //
        // Moving the folder relocates the physical files, but a LiteDB record that stored an
        // *absolute* path computed before the rename (BoxArt's cached-image paths, and an
        // auto-installed Emulator's own ExecutablePath - both built from the old AppDataPath at
        // the time they were written) still contains the literal old prefix. Found via a real
        // interactive run, not anticipated in the original design: box art failed to load with a
        // DirectoryNotFoundException pointing at the old %LOCALAPPDATA%\Bridge\ImageCache\...
        // path, even though the folder move itself had already succeeded.
        RewriteStoredAbsolutePaths(Path.Combine(_newPath, "emubridge.db"));
    }

    private void RewriteStoredAbsolutePaths(string dbPath)
    {
        if (!File.Exists(dbPath))
        {
            return;
        }

        try
        {
            using var db = new LiteDatabase(dbPath);

            // Must run before any strongly-typed read of "emulators" below - LiteDB deserializes
            // enums by name, and a real, already-auto-installed emulator's InstallSource can still
            // hold the literal string "BridgeManaged" on disk (confirmed via a real
            // ArgumentException during interactive testing: "Requested value 'BridgeManaged' was
            // not found"). The enum member itself was renamed to EmuBridgeManaged, so a
            // strongly-typed GetCollection<Emulator>() read throws on that row before this
            // migration ever gets a chance to look at it - fixed here via LiteDB's untyped
            // BsonDocument API, which never attempts enum parsing at all.
            RewriteLegacyInstallSourceValues(db);

            var boxArtCollection = db.GetCollection<BoxArt>("boxArt");
            foreach (var record in boxArtCollection.FindAll().ToList())
            {
                record.LocalPath = RewriteNullablePathPrefix(record.LocalPath);
                record.VerticalLocalPath = RewriteNullablePathPrefix(record.VerticalLocalPath);
                record.ScreenshotLocalPaths = record.ScreenshotLocalPaths.Select(RewritePathPrefix).ToList();
                boxArtCollection.Update(record);
            }

            // Only rewrites entries whose ExecutablePath actually starts with the old AppData
            // prefix (i.e. was auto-installed under it, ARCHITECTURE.md -> ADR-11/ADR-14) - a
            // user-pointed emulator living outside AppData entirely is left untouched, since its
            // path was never under the folder that just moved.
            var emulatorsCollection = db.GetCollection<Emulator>("emulators");
            foreach (var record in emulatorsCollection.FindAll().ToList())
            {
                var rewritten = RewritePathPrefix(record.ExecutablePath);
                if (rewritten != record.ExecutablePath)
                {
                    record.ExecutablePath = rewritten;
                    emulatorsCollection.Update(record);
                }
            }
        }
        catch (Exception ex)
        {
            // Deliberately catches everything, not just IOException/LiteException - this step
            // runs during App.OnStartup, before the main window exists, so anything it doesn't
            // catch crashes the entire app before the user ever sees a window (confirmed for real:
            // the InstallSource fix above exists because the original narrower catch didn't cover
            // the ArgumentException that scenario actually threw). A game's box art or an
            // auto-installed emulator failing to resolve afterward is recoverable (re-fetch,
            // re-configure); the app failing to start at all is not.
            _logger.LogWarning(
                ex,
                "Migrated the app data folder but could not rewrite stored data inside {DbPath}; cached images and any auto-installed emulator may need to be re-fetched/reconfigured.",
                dbPath);
        }
    }

    // Fixes a real, confirmed compatibility break the enum rename itself caused: LiteDB
    // deserializes enums by name, so a pre-existing Emulator row whose InstallSource was saved as
    // the literal string "BridgeManaged" throws ArgumentException the moment anything tries to
    // read it through the strongly-typed Emulator model, since that enum member is now named
    // EmuBridgeManaged. Rewriting the raw BSON field directly (never touching the strongly-typed
    // Emulator class) is what makes it safe to read normally afterward.
    private static void RewriteLegacyInstallSourceValues(LiteDatabase db)
    {
        var rawEmulators = db.GetCollection("emulators");
        foreach (var doc in rawEmulators.FindAll().ToList())
        {
            if (doc.TryGetValue("InstallSource", out var installSource) && installSource.AsString == "BridgeManaged")
            {
                doc["InstallSource"] = "EmuBridgeManaged";
                rawEmulators.Update(doc);
            }
        }
    }

    private string? RewriteNullablePathPrefix(string? path) => path is null ? null : RewritePathPrefix(path);

    private string RewritePathPrefix(string path) => path.StartsWith(_oldPath, StringComparison.OrdinalIgnoreCase)
        ? _newPath + path[_oldPath.Length..]
        : path;
}
