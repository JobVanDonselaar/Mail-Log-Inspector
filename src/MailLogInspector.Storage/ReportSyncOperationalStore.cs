using System.Globalization;
using Microsoft.Data.Sqlite;

namespace MailLogInspector.Storage;

public sealed class ReportSyncOperationalStore
{
    private readonly string _databasePath;

    public ReportSyncOperationalStore(string databasePath)
    {
        _databasePath = Path.GetFullPath(databasePath);
    }

    public void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS report_sync_config (
                singleton_id INTEGER PRIMARY KEY CHECK (singleton_id = 1),
                mode TEXT NOT NULL,
                last_attempt_at_utc TEXT NULL,
                last_success_at_utc TEXT NULL,
                auto_sync_enabled INTEGER NOT NULL DEFAULT 0,
                close_to_tray_enabled INTEGER NOT NULL DEFAULT 0,
                general_settings_version INTEGER NOT NULL DEFAULT 0
            );


            CREATE TABLE IF NOT EXISTS report_import_sources (
                source_hash TEXT PRIMARY KEY COLLATE NOCASE,
                source TEXT NOT NULL,
                file_name TEXT NOT NULL,
                report_day TEXT NULL,
                recorded_at_utc TEXT NOT NULL
            ) WITHOUT ROWID;

            CREATE INDEX IF NOT EXISTS ix_report_import_sources_recorded
                ON report_import_sources(recorded_at_utc DESC);
            """;
        command.ExecuteNonQuery();
        EnsureColumnExists(connection, "report_sync_config", "auto_sync_enabled", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumnExists(connection, "report_sync_config", "close_to_tray_enabled", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumnExists(connection, "report_sync_config", "general_settings_version", "INTEGER NOT NULL DEFAULT 0");
        using (SqliteCommand seedCommand = connection.CreateCommand())
        {
            seedCommand.CommandText = """
                INSERT OR IGNORE INTO report_sync_config (
                    singleton_id,
                    mode,
                    last_attempt_at_utc,
                    last_success_at_utc,
                    auto_sync_enabled,
                    close_to_tray_enabled,
                    general_settings_version
                )
                VALUES (1, 'gmail-only', NULL, NULL, 0, 0, 0);
                """;
            seedCommand.ExecuteNonQuery();
        }
        MigrateLegacyGeneralSettings(connection);
    }

    public ReportSyncConfig LoadConfig()
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT mode, last_attempt_at_utc, last_success_at_utc,
                   auto_sync_enabled, close_to_tray_enabled
            FROM report_sync_config
            WHERE singleton_id = 1;
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return ReportSyncConfig.Default;
        }

        return new ReportSyncConfig(
            ReportSyncMode.Normalize(reader.GetString(0)),
            ReadNullableDateTime(reader, 1),
            ReadNullableDateTime(reader, 2),
            !reader.IsDBNull(3) && reader.GetInt32(3) == 1,
            !reader.IsDBNull(4) && reader.GetInt32(4) == 1);
    }

    public void SaveConfig(ReportSyncConfig config)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO report_sync_config (
                singleton_id,
                mode,
                last_attempt_at_utc,
                last_success_at_utc,
                auto_sync_enabled,
                close_to_tray_enabled,
                general_settings_version
            )
            VALUES (1, $mode, $lastAttempt, $lastSuccess, $autoSync, $closeToTray, 1)
            ON CONFLICT(singleton_id) DO UPDATE SET
                mode = excluded.mode,
                last_attempt_at_utc = excluded.last_attempt_at_utc,
                last_success_at_utc = excluded.last_success_at_utc,
                auto_sync_enabled = excluded.auto_sync_enabled,
                close_to_tray_enabled = excluded.close_to_tray_enabled,
                general_settings_version = 1;
            """;
        command.Parameters.AddWithValue("$mode", ReportSyncMode.Normalize(config.Mode));
        command.Parameters.AddWithValue("$lastAttempt", FormatNullableDateTime(config.LastAttemptAtUtc));
        command.Parameters.AddWithValue("$lastSuccess", FormatNullableDateTime(config.LastSuccessAtUtc));
        command.Parameters.AddWithValue("$autoSync", config.AutoSyncEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$closeToTray", config.CloseToTrayEnabled ? 1 : 0);
        command.ExecuteNonQuery();
    }

    public void RecordAttempt(string mode, DateTime attemptedAtUtc)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE report_sync_config
            SET mode = $mode,
                last_attempt_at_utc = $attemptedAtUtc
            WHERE singleton_id = 1;
            """;
        command.Parameters.AddWithValue("$mode", ReportSyncMode.Normalize(mode));
        command.Parameters.AddWithValue(
            "$attemptedAtUtc",
            NormalizeUtc(attemptedAtUtc).ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    public void RecordSuccess(DateTime succeededAtUtc)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE report_sync_config
            SET last_success_at_utc = $succeededAtUtc
            WHERE singleton_id = 1;
            """;
        command.Parameters.AddWithValue(
            "$succeededAtUtc",
            NormalizeUtc(succeededAtUtc).ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }
    public void RecordImportSource(ReportImportSourceRow row)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO report_import_sources (
                source_hash,
                source,
                file_name,
                report_day,
                recorded_at_utc
            )
            VALUES ($hash, $source, $fileName, $reportDay, $recordedAt);
            """;
        command.Parameters.AddWithValue("$hash", row.SourceHash);
        command.Parameters.AddWithValue("$source", row.Source);
        command.Parameters.AddWithValue("$fileName", row.FileName);
        command.Parameters.AddWithValue(
            "$reportDay",
            row.ReportDay.HasValue
                ? row.ReportDay.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : DBNull.Value);
        command.Parameters.AddWithValue("$recordedAt", NormalizeUtc(row.RecordedAtUtc).ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    public ReportImportSourceRow? FindImportSource(string sourceHash)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT source_hash, source, file_name, report_day, recorded_at_utc
            FROM report_import_sources
            WHERE source_hash = $hash
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$hash", sourceHash);
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? ReadRow(reader) : null;
    }

    public IReadOnlyList<ReportImportSourceRow> ReadImportSources(int limit)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT source_hash, source, file_name, report_day, recorded_at_utc
            FROM report_import_sources
            ORDER BY recorded_at_utc DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", Math.Max(1, limit));
        using SqliteDataReader reader = command.ExecuteReader();
        List<ReportImportSourceRow> rows = [];
        while (reader.Read())
        {
            rows.Add(ReadRow(reader));
        }

        return rows;
    }

    private static void MigrateLegacyGeneralSettings(SqliteConnection connection)
    {
        using SqliteCommand versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT general_settings_version FROM report_sync_config WHERE singleton_id = 1;";
        int version = Convert.ToInt32(versionCommand.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
        if (version >= 1)
        {
            return;
        }

        bool hasLegacySettings = TableExists(connection, "gmail_report_config") &&
                                 ColumnExists(connection, "gmail_report_config", "auto_sync_enabled") &&
                                 ColumnExists(connection, "gmail_report_config", "close_to_tray_enabled");
        using SqliteCommand migrateCommand = connection.CreateCommand();
        migrateCommand.CommandText = hasLegacySettings
            ? """
                UPDATE report_sync_config
                SET auto_sync_enabled = COALESCE(
                        (SELECT auto_sync_enabled FROM gmail_report_config WHERE config_id = 1),
                        auto_sync_enabled),
                    close_to_tray_enabled = COALESCE(
                        (SELECT close_to_tray_enabled FROM gmail_report_config WHERE config_id = 1),
                        close_to_tray_enabled),
                    general_settings_version = 1
                WHERE singleton_id = 1;
                """
            : """
                UPDATE report_sync_config
                SET general_settings_version = 1
                WHERE singleton_id = 1;
                """;
        migrateCommand.ExecuteNonQuery();
    }

    private static void EnsureColumnExists(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string definition)
    {
        if (ColumnExists(connection, tableName, columnName))
        {
            return;
        }

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};";
        command.ExecuteNonQuery();
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $tableName;";
        command.Parameters.AddWithValue("$tableName", tableName);
        return Convert.ToInt32(command.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture) > 0;
    }

    private static bool ColumnExists(SqliteConnection connection, string tableName, string columnName)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        return connection;
    }

    private static ReportImportSourceRow ReadRow(SqliteDataReader reader)
    {
        return new ReportImportSourceRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3)
                ? null
                : DateTime.ParseExact(reader.GetString(3), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    private static object FormatNullableDateTime(DateTime? value)
    {
        return value.HasValue
            ? NormalizeUtc(value.Value).ToString("O", CultureInfo.InvariantCulture)
            : DBNull.Value;
    }

    private static DateTime? ReadNullableDateTime(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : DateTime.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static DateTime NormalizeUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}
