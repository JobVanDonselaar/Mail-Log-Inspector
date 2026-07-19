using MailLogInspector.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class ReportSyncOperationalStoreTests
{
    [Fact]
    public void NewStoreDefaultsToGmailOnly()
    {
        ReportSyncOperationalStore store = CreateStore();

        ReportSyncConfig config = store.LoadConfig();

        Assert.Equal(ReportSyncMode.GmailOnly, config.Mode);
        Assert.False(config.AutoSyncEnabled);
        Assert.False(config.CloseToTrayEnabled);
        Assert.Null(config.LastAttemptAtUtc);
        Assert.Null(config.LastSuccessAtUtc);
    }

    [Theory]
    [InlineData(ReportSyncMode.DirectWithGmailFallback)]
    [InlineData(ReportSyncMode.GmailOnly)]
    [InlineData(ReportSyncMode.DirectOnly)]
    public void SaveConfigRoundTripsAllModes(string mode)
    {
        ReportSyncOperationalStore store = CreateStore();
        DateTime attempt = new(2026, 7, 19, 1, 0, 0, DateTimeKind.Utc);
        DateTime success = attempt.AddMinutes(1);

        store.SaveConfig(new ReportSyncConfig(mode, attempt, success, true, true));

        Assert.Equal(new ReportSyncConfig(mode, attempt, success, true, true), store.LoadConfig());
    }

    [Fact]
    public void RunTimestampsDoNotOverwriteGeneralSettings()
    {
        ReportSyncOperationalStore store = CreateStore();
        store.SaveConfig(new ReportSyncConfig(ReportSyncMode.DirectOnly, null, null, true, true));
        DateTime attempt = new(2026, 7, 19, 1, 0, 0, DateTimeKind.Utc);
        DateTime success = attempt.AddMinutes(2);

        store.RecordAttempt(ReportSyncMode.DirectOnly, attempt);
        store.RecordSuccess(success);

        ReportSyncConfig result = store.LoadConfig();
        Assert.Equal(attempt, result.LastAttemptAtUtc);
        Assert.Equal(success, result.LastSuccessAtUtc);
        Assert.True(result.AutoSyncEnabled);
        Assert.True(result.CloseToTrayEnabled);
    }

    [Fact]
    public void InitializeMigratesLegacyGeneralSettingsFromGmailConfigOnce()
    {
        string root = Path.Combine(Path.GetTempPath(), "mail-log-inspector-sync-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string databasePath = Path.Combine(root, "operational.sqlite");
        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE gmail_report_config (
                    config_id INTEGER PRIMARY KEY,
                    auto_sync_enabled INTEGER NOT NULL,
                    close_to_tray_enabled INTEGER NOT NULL
                );
                INSERT INTO gmail_report_config VALUES (1, 1, 1);

                CREATE TABLE report_sync_config (
                    singleton_id INTEGER PRIMARY KEY,
                    mode TEXT NOT NULL,
                    last_attempt_at_utc TEXT NULL,
                    last_success_at_utc TEXT NULL
                );
                INSERT INTO report_sync_config VALUES (1, 'direct-only', NULL, NULL);
                """;
            command.ExecuteNonQuery();
        }

        var store = new ReportSyncOperationalStore(databasePath);
        store.Initialize();
        ReportSyncConfig migrated = store.LoadConfig();
        Assert.True(migrated.AutoSyncEnabled);
        Assert.True(migrated.CloseToTrayEnabled);

        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE gmail_report_config
                SET auto_sync_enabled = 0,
                    close_to_tray_enabled = 0
                WHERE config_id = 1;
                """;
            command.ExecuteNonQuery();
        }

        store.Initialize();
        ReportSyncConfig afterSecondInitialize = store.LoadConfig();
        Assert.True(afterSecondInitialize.AutoSyncEnabled);
        Assert.True(afterSecondInitialize.CloseToTrayEnabled);
    }

    [Fact]
    public void ImportSourceRoundTripsByHash()
    {
        ReportSyncOperationalStore store = CreateStore();
        var row = new ReportImportSourceRow(
            "HASH-PORTAL",
            ReportImportSource.SmtpDirect,
            "NextGen_2026-07-18.zip",
            new DateTime(2026, 7, 18),
            new DateTime(2026, 7, 19, 1, 2, 0, DateTimeKind.Utc));

        store.RecordImportSource(row);

        Assert.Equal(row, Assert.Single(store.ReadImportSources(10)));
        Assert.Equal(row, store.FindImportSource("hash-portal"));
    }

    private static ReportSyncOperationalStore CreateStore()
    {
        string root = Path.Combine(Path.GetTempPath(), "mail-log-inspector-sync-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new ReportSyncOperationalStore(Path.Combine(root, "operational.sqlite"));
        store.Initialize();
        return store;
    }
}
