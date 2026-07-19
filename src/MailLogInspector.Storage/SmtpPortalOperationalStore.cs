using System.Globalization;
using Microsoft.Data.Sqlite;

namespace MailLogInspector.Storage;

public sealed class SmtpPortalOperationalStore
{
    private readonly string _databasePath;

    public SmtpPortalOperationalStore(string databasePath)
    {
        _databasePath = Path.GetFullPath(databasePath);
    }

    public void Initialize()
    {
        string? directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using SqliteConnection connection = OpenConnection();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS smtp_portal_config (
                    config_id INTEGER PRIMARY KEY CHECK (config_id = 1),
                    username TEXT NULL,
                    encrypted_password TEXT NULL,
                    encrypted_totp_secret TEXT NULL,
                    connection_status TEXT NULL,
                    last_probe_at_utc TEXT NULL,
                    use_default_report_syntax INTEGER NOT NULL DEFAULT 1,
                    custom_report_syntax TEXT NULL,
                    last_successful_portal_use_at_utc TEXT NULL
                );

                CREATE TABLE IF NOT EXISTS smtp_portal_probe_history (
                    report_name TEXT NOT NULL PRIMARY KEY,
                    period_start TEXT NOT NULL,
                    period_end TEXT NOT NULL,
                    source_hash TEXT NOT NULL,
                    local_path TEXT NOT NULL,
                    file_size INTEGER NOT NULL,
                    already_imported INTEGER NOT NULL,
                    status TEXT NOT NULL,
                    error_text TEXT NULL,
                    attempted_at_utc TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        using SqliteTransaction transaction = connection.BeginTransaction();
        EnsureColumnExists(
            connection,
            transaction,
            "use_default_report_syntax",
            "ALTER TABLE smtp_portal_config ADD COLUMN use_default_report_syntax INTEGER NOT NULL DEFAULT 1;");
        EnsureColumnExists(
            connection,
            transaction,
            "custom_report_syntax",
            "ALTER TABLE smtp_portal_config ADD COLUMN custom_report_syntax TEXT NULL;");
        EnsureColumnExists(
            connection,
            transaction,
            "last_successful_portal_use_at_utc",
            "ALTER TABLE smtp_portal_config ADD COLUMN last_successful_portal_use_at_utc TEXT NULL;");
        transaction.Commit();
    }

    public SmtpPortalConfig LoadConfig()
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT username,
                   encrypted_password,
                   encrypted_totp_secret,
                   connection_status,
                   last_probe_at_utc,
                   use_default_report_syntax,
                   custom_report_syntax,
                   last_successful_portal_use_at_utc
            FROM smtp_portal_config
            WHERE config_id = 1;
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? new SmtpPortalConfig(
                ReadNullableString(reader, 0),
                ReadNullableString(reader, 1),
                ReadNullableString(reader, 2),
                ReadNullableString(reader, 3),
                ReadNullableDateTime(reader, 4),
                reader.GetInt32(5) == 1,
                ReadNullableString(reader, 6),
                ReadNullableDateTime(reader, 7))
            : SmtpPortalConfig.Empty;
    }

    public void SaveConfig(SmtpPortalConfig config)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO smtp_portal_config (
                config_id,
                username,
                encrypted_password,
                encrypted_totp_secret,
                connection_status,
                last_probe_at_utc,
                use_default_report_syntax,
                custom_report_syntax,
                last_successful_portal_use_at_utc
            )
            VALUES (
                1,
                $username,
                $encryptedPassword,
                $encryptedTotpSecret,
                $connectionStatus,
                $lastProbeAtUtc,
                $useDefaultReportSyntax,
                $customReportSyntax,
                $lastSuccessfulPortalUseAtUtc
            )
            ON CONFLICT(config_id) DO UPDATE SET
                username = excluded.username,
                encrypted_password = excluded.encrypted_password,
                encrypted_totp_secret = excluded.encrypted_totp_secret,
                connection_status = excluded.connection_status,
                last_probe_at_utc = excluded.last_probe_at_utc,
                use_default_report_syntax = excluded.use_default_report_syntax,
                custom_report_syntax = excluded.custom_report_syntax,
                last_successful_portal_use_at_utc = excluded.last_successful_portal_use_at_utc;
            """;
        command.Parameters.AddWithValue("$username", (object?)config.Username ?? DBNull.Value);
        command.Parameters.AddWithValue("$encryptedPassword", (object?)config.EncryptedPassword ?? DBNull.Value);
        command.Parameters.AddWithValue("$encryptedTotpSecret", (object?)config.EncryptedTotpSecret ?? DBNull.Value);
        command.Parameters.AddWithValue("$connectionStatus", (object?)config.ConnectionStatus ?? DBNull.Value);
        command.Parameters.AddWithValue("$lastProbeAtUtc", ToDbValue(config.LastProbeAtUtc));
        command.Parameters.AddWithValue("$useDefaultReportSyntax", config.UseDefaultReportSyntax ? 1 : 0);
        command.Parameters.AddWithValue("$customReportSyntax", (object?)config.CustomReportSyntax ?? DBNull.Value);
        command.Parameters.AddWithValue("$lastSuccessfulPortalUseAtUtc", ToDbValue(config.LastSuccessfulPortalUseAtUtc));
        command.ExecuteNonQuery();
    }

    public void RecordSuccessfulPortalUse(DateTime usedAtUtc)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO smtp_portal_config (
                config_id,
                last_successful_portal_use_at_utc
            )
            VALUES (1, $usedAtUtc)
            ON CONFLICT(config_id) DO UPDATE SET
                last_successful_portal_use_at_utc = excluded.last_successful_portal_use_at_utc;
            """;
        command.Parameters.AddWithValue("$usedAtUtc", usedAtUtc.ToUniversalTime().ToString("O"));
        command.ExecuteNonQuery();
    }

    public void UpsertProbeHistory(SmtpPortalProbeHistoryRow row)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO smtp_portal_probe_history (
                report_name,
                period_start,
                period_end,
                source_hash,
                local_path,
                file_size,
                already_imported,
                status,
                error_text,
                attempted_at_utc
            )
            VALUES (
                $reportName,
                $periodStart,
                $periodEnd,
                $sourceHash,
                $localPath,
                $fileSize,
                $alreadyImported,
                $status,
                $errorText,
                $attemptedAtUtc
            )
            ON CONFLICT(report_name) DO UPDATE SET
                period_start = excluded.period_start,
                period_end = excluded.period_end,
                source_hash = excluded.source_hash,
                local_path = excluded.local_path,
                file_size = excluded.file_size,
                already_imported = excluded.already_imported,
                status = excluded.status,
                error_text = excluded.error_text,
                attempted_at_utc = excluded.attempted_at_utc;
            """;
        command.Parameters.AddWithValue("$reportName", row.ReportName);
        command.Parameters.AddWithValue("$periodStart", row.PeriodStart.ToString("O"));
        command.Parameters.AddWithValue("$periodEnd", row.PeriodEnd.ToString("O"));
        command.Parameters.AddWithValue("$sourceHash", row.SourceHash);
        command.Parameters.AddWithValue("$localPath", row.LocalPath);
        command.Parameters.AddWithValue("$fileSize", row.FileSize);
        command.Parameters.AddWithValue("$alreadyImported", row.AlreadyImported ? 1 : 0);
        command.Parameters.AddWithValue("$status", row.Status);
        command.Parameters.AddWithValue("$errorText", (object?)row.ErrorText ?? DBNull.Value);
        command.Parameters.AddWithValue("$attemptedAtUtc", row.AttemptedAtUtc.ToString("O"));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<SmtpPortalProbeHistoryRow> ReadProbeHistory(int limit)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT report_name,
                   period_start,
                   period_end,
                   source_hash,
                   local_path,
                   file_size,
                   already_imported,
                   status,
                   error_text,
                   attempted_at_utc
            FROM smtp_portal_probe_history
            ORDER BY attempted_at_utc DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", Math.Max(1, limit));
        using SqliteDataReader reader = command.ExecuteReader();
        List<SmtpPortalProbeHistoryRow> rows = new();
        while (reader.Read())
        {
            rows.Add(new SmtpPortalProbeHistoryRow(
                reader.GetString(0),
                DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt64(5),
                reader.GetInt32(6) == 1,
                reader.GetString(7),
                ReadNullableString(reader, 8),
                DateTime.Parse(reader.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return rows;
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        return connection;
    }

    private static void EnsureColumnExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string columnName,
        string alterSql)
    {
        bool exists;
        using (SqliteCommand inspect = connection.CreateCommand())
        {
            inspect.Transaction = transaction;
            inspect.CommandText = "PRAGMA table_info(smtp_portal_config);";
            using SqliteDataReader reader = inspect.ExecuteReader();
            exists = false;
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (exists)
        {
            return;
        }

        using SqliteCommand alter = connection.CreateCommand();
        alter.Transaction = transaction;
        alter.CommandText = alterSql;
        alter.ExecuteNonQuery();
    }

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTime? ReadNullableDateTime(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : DateTime.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static object ToDbValue(DateTime? value)
    {
        return value.HasValue ? value.Value.ToUniversalTime().ToString("O") : DBNull.Value;
    }
}