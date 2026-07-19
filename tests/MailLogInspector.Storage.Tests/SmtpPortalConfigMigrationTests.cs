using MailLogInspector.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class SmtpPortalConfigMigrationTests
{
    [Fact]
    public void Config_RoundTripsSyntaxAndLastSuccessfulPortalUse()
    {
        string databasePath = CreateDatabasePath();
        var store = new SmtpPortalOperationalStore(databasePath);
        store.Initialize();
        var config = new SmtpPortalConfig(
            "portal-user@example.test",
            "encrypted-password",
            "encrypted-totp",
            "Gekoppeld",
            new DateTime(2026, 7, 19, 8, 0, 0, DateTimeKind.Utc),
            UseDefaultReportSyntax: false,
            CustomReportSyntax: "Exquise_{start}_{end}",
            LastSuccessfulPortalUseAtUtc: new DateTime(2026, 7, 19, 8, 5, 0, DateTimeKind.Utc));

        store.SaveConfig(config);

        Assert.Equal(config, store.LoadConfig());
    }

    [Fact]
    public void Initialize_MigratesLegacyConfigIdempotentlyToDefaultSyntax()
    {
        string databasePath = CreateDatabasePath();
        CreateLegacyConfig(databasePath);
        var store = new SmtpPortalOperationalStore(databasePath);

        store.Initialize();
        store.Initialize();

        SmtpPortalConfig config = store.LoadConfig();
        Assert.Equal("legacy-user@example.test", config.Username);
        Assert.True(config.UseDefaultReportSyntax);
        Assert.Null(config.CustomReportSyntax);
        Assert.Null(config.LastSuccessfulPortalUseAtUtc);
        Assert.Equal(
            [
                "config_id",
                "username",
                "encrypted_password",
                "encrypted_totp_secret",
                "connection_status",
                "last_probe_at_utc",
                "use_default_report_syntax",
                "custom_report_syntax",
                "last_successful_portal_use_at_utc"
            ],
            ReadColumnNames(databasePath));
    }

    [Fact]
    public void RecordSuccessfulPortalUse_PreservesAllOtherConfigValues()
    {
        string databasePath = CreateDatabasePath();
        var store = new SmtpPortalOperationalStore(databasePath);
        store.Initialize();
        var before = new SmtpPortalConfig(
            "portal-user@example.test",
            "encrypted-password",
            "encrypted-totp",
            "Proefdownload geslaagd",
            new DateTime(2026, 7, 19, 8, 0, 0, DateTimeKind.Utc),
            UseDefaultReportSyntax: false,
            CustomReportSyntax: "Exquise_{start}_{end}");
        DateTime usedAtUtc = new(2026, 7, 19, 8, 10, 0, DateTimeKind.Utc);
        store.SaveConfig(before);

        store.RecordSuccessfulPortalUse(usedAtUtc);

        Assert.Equal(
            before with { LastSuccessfulPortalUseAtUtc = usedAtUtc },
            store.LoadConfig());
    }

    private static string CreateDatabasePath()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "mail-log-inspector-smtp-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return Path.Combine(root, "operational.sqlite");
    }

    private static void CreateLegacyConfig(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE smtp_portal_config (
                config_id INTEGER PRIMARY KEY CHECK (config_id = 1),
                username TEXT NULL,
                encrypted_password TEXT NULL,
                encrypted_totp_secret TEXT NULL,
                connection_status TEXT NULL,
                last_probe_at_utc TEXT NULL
            );

            INSERT INTO smtp_portal_config (
                config_id,
                username,
                encrypted_password,
                encrypted_totp_secret,
                connection_status,
                last_probe_at_utc
            )
            VALUES (
                1,
                'legacy-user@example.test',
                'encrypted-password',
                'encrypted-totp',
                'Getest',
                NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private static IReadOnlyList<string> ReadColumnNames(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(smtp_portal_config);";
        using SqliteDataReader reader = command.ExecuteReader();
        List<string> names = [];
        while (reader.Read())
        {
            names.Add(reader.GetString(1));
        }

        return names;
    }
}
