using System.Data.Common;
using System.Globalization;
using MailLogInspector.Core;
using Microsoft.Data.Sqlite;

namespace MailLogInspector.Storage;

public sealed class GmailReportOperationalStore
{
    private readonly string _databasePath;

    public GmailReportOperationalStore(string databasePath)
    {
        _databasePath = Path.GetFullPath(databasePath);
    }

    public void Initialize()
    {
        string? directoryName = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directoryName))
        {
            Directory.CreateDirectory(directoryName);
        }

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS gmail_report_config (
                config_id INTEGER PRIMARY KEY CHECK (config_id = 1),
                account_email_address TEXT NULL,
                authentication_mode TEXT NULL,
                client_id TEXT NULL,
                client_secret TEXT NULL,
                encrypted_refresh_token TEXT NULL,
                encrypted_app_password TEXT NULL,
                auto_sync_enabled INTEGER NOT NULL DEFAULT 0,
                auto_sync_interval_minutes INTEGER NOT NULL DEFAULT 15,
                last_auto_sync_at_utc TEXT NULL,
                connected_at_utc TEXT NULL,
                last_token_refresh_at_utc TEXT NULL,
                connection_status TEXT NULL,
                close_to_tray_enabled INTEGER NOT NULL DEFAULT 0,
                imap_provider TEXT NOT NULL DEFAULT 'gmail',
                imap_host TEXT NULL,
                imap_port INTEGER NOT NULL DEFAULT 993,
                imap_use_ssl INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS gmail_report_history (
                gmail_message_id TEXT NOT NULL PRIMARY KEY,
                gmail_internal_date_utc TEXT NOT NULL,
                sender TEXT NOT NULL,
                subject TEXT NOT NULL,
                zip_url TEXT NOT NULL,
                source_hash TEXT NULL,
                download_status TEXT NOT NULL,
                import_status TEXT NOT NULL,
                archived INTEGER NOT NULL,
                error_text TEXT NULL,
                first_seen_at_utc TEXT NOT NULL,
                last_attempt_at_utc TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_gmail_report_history_zip_url_success
                ON gmail_report_history(zip_url)
                WHERE import_status = 'ok';
            """;
        command.ExecuteNonQuery();
        EnsureColumnExists(connection, "gmail_report_config", "authentication_mode", "TEXT NULL");
        EnsureColumnExists(connection, "gmail_report_config", "encrypted_app_password", "TEXT NULL");
        EnsureColumnExists(connection, "gmail_report_config", "auto_sync_enabled", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumnExists(connection, "gmail_report_config", "auto_sync_interval_minutes", "INTEGER NOT NULL DEFAULT 15");
        EnsureColumnExists(connection, "gmail_report_config", "last_auto_sync_at_utc", "TEXT NULL");
        EnsureColumnExists(connection, "gmail_report_config", "close_to_tray_enabled", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumnExists(connection, "gmail_report_config", "imap_provider", "TEXT NOT NULL DEFAULT 'gmail'");
        EnsureColumnExists(connection, "gmail_report_config", "imap_host", "TEXT NULL");
        EnsureColumnExists(connection, "gmail_report_config", "imap_port", "INTEGER NOT NULL DEFAULT 993");
        EnsureColumnExists(connection, "gmail_report_config", "imap_use_ssl", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumnExists(connection, "gmail_report_history", "source_hash", "TEXT NULL");
    }

    public GmailReportConfig LoadConfig()
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT account_email_address,
                   authentication_mode,
                   client_id,
                   client_secret,
                   encrypted_refresh_token,
                   encrypted_app_password,
                   connected_at_utc,
                   last_token_refresh_at_utc,
                   connection_status,
                   imap_provider,
                   imap_host,
                   imap_port,
                   imap_use_ssl
            FROM gmail_report_config
            WHERE config_id = 1;
            """;

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return GmailReportConfig.Empty;
        }

        return new GmailReportConfig(
            ReadNullableString(reader, 0),
            ReadNullableString(reader, 1) ?? GmailAuthenticationMode.OAuth,
            ReadNullableString(reader, 2),
            ReadNullableString(reader, 3),
            ReadNullableString(reader, 4),
            ReadNullableString(reader, 5),
            ReadNullableDateTime(reader, 6),
            ReadNullableDateTime(reader, 7),
            ReadNullableString(reader, 8),
            ImapProvider.Normalize(ReadNullableString(reader, 9)),
            ReadNullableString(reader, 10),
            reader.IsDBNull(11) ? 993 : Math.Max(1, reader.GetInt32(11)),
            reader.IsDBNull(12) || reader.GetInt32(12) == 1);
    }

    public void SaveConfig(GmailReportConfig config)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO gmail_report_config (
                config_id,
                account_email_address,
                authentication_mode,
                client_id,
                client_secret,
                encrypted_refresh_token,
                encrypted_app_password,
                connected_at_utc,
                last_token_refresh_at_utc,
                connection_status,
                imap_provider,
                imap_host,
                imap_port,
                imap_use_ssl
            )
            VALUES (
                1,
                $accountEmailAddress,
                $authenticationMode,
                $clientId,
                $clientSecret,
                $encryptedRefreshToken,
                $encryptedAppPassword,
                $connectedAtUtc,
                $lastTokenRefreshAtUtc,
                $connectionStatus,
                $imapProvider,
                $imapHost,
                $imapPort,
                $imapUseSsl
            )
            ON CONFLICT(config_id) DO UPDATE SET
                account_email_address = excluded.account_email_address,
                authentication_mode = excluded.authentication_mode,
                client_id = excluded.client_id,
                client_secret = excluded.client_secret,
                encrypted_refresh_token = excluded.encrypted_refresh_token,
                encrypted_app_password = excluded.encrypted_app_password,
                connected_at_utc = excluded.connected_at_utc,
                last_token_refresh_at_utc = excluded.last_token_refresh_at_utc,
                connection_status = excluded.connection_status,
                imap_provider = excluded.imap_provider,
                imap_host = excluded.imap_host,
                imap_port = excluded.imap_port,
                imap_use_ssl = excluded.imap_use_ssl;
            """;
        command.Parameters.AddWithValue("$accountEmailAddress", (object?)config.AccountEmailAddress ?? DBNull.Value);
        command.Parameters.AddWithValue("$authenticationMode", (object?)config.AuthenticationMode ?? GmailAuthenticationMode.OAuth);
        command.Parameters.AddWithValue("$clientId", (object?)config.ClientId ?? DBNull.Value);
        command.Parameters.AddWithValue("$clientSecret", (object?)config.ClientSecret ?? DBNull.Value);
        command.Parameters.AddWithValue("$encryptedRefreshToken", (object?)config.EncryptedRefreshToken ?? DBNull.Value);
        command.Parameters.AddWithValue("$encryptedAppPassword", (object?)config.EncryptedAppPassword ?? DBNull.Value);
        command.Parameters.AddWithValue("$connectedAtUtc", config.ConnectedAtUtc.HasValue ? config.ConnectedAtUtc.Value.ToString("O") : DBNull.Value);
        command.Parameters.AddWithValue("$lastTokenRefreshAtUtc", config.LastTokenRefreshAtUtc.HasValue ? config.LastTokenRefreshAtUtc.Value.ToString("O") : DBNull.Value);
        command.Parameters.AddWithValue("$connectionStatus", (object?)config.ConnectionStatus ?? DBNull.Value);
        command.Parameters.AddWithValue("$imapProvider", ImapProvider.Normalize(config.ImapProvider));
        command.Parameters.AddWithValue("$imapHost", (object?)config.ImapHost ?? DBNull.Value);
        command.Parameters.AddWithValue("$imapPort", Math.Max(1, config.ImapPort));
        command.Parameters.AddWithValue("$imapUseSsl", config.ImapUseSsl ? 1 : 0);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<GmailReportHistoryRow> ReadHistory(int limit)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT gmail_message_id,
                   gmail_internal_date_utc,
                   sender,
                   subject,
                   zip_url,
                   download_status,
                   import_status,
                   archived,
                   error_text,
                   first_seen_at_utc,
                   last_attempt_at_utc,
                   source_hash
            FROM gmail_report_history
            ORDER BY gmail_internal_date_utc DESC, last_attempt_at_utc DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        using SqliteDataReader reader = command.ExecuteReader();
        List<GmailReportHistoryRow> rows = new();
        while (reader.Read())
        {
            rows.Add(ReadHistoryRow(reader));
        }

        return rows;
    }

    public void BackfillMissingSourceHashes(IReadOnlyList<MailLogInspectorImportedFile> imports)
    {
        Dictionary<string, string> uniqueHashesByFileName = imports
            .Where(import => !string.IsNullOrWhiteSpace(import.SourceFileName) && !string.IsNullOrWhiteSpace(import.SourceHash))
            .GroupBy(import => import.SourceFileName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().SourceHash, StringComparer.OrdinalIgnoreCase);
        if (uniqueHashesByFileName.Count == 0)
        {
            return;
        }

        using SqliteConnection connection = OpenConnection();
        List<(string MessageId, string SourceHash)> updates = new();
        using (SqliteCommand readCommand = connection.CreateCommand())
        {
            readCommand.CommandText = """
                SELECT gmail_message_id, zip_url
                FROM gmail_report_history
                WHERE import_status = 'ok'
                  AND (source_hash IS NULL OR TRIM(source_hash) = '');
                """;
            using SqliteDataReader reader = readCommand.ExecuteReader();
            while (reader.Read())
            {
                string fileName = TryReadZipFileName(reader.GetString(1));
                if (uniqueHashesByFileName.TryGetValue(fileName, out string? sourceHash))
                {
                    updates.Add((reader.GetString(0), sourceHash));
                }
            }
        }

        using SqliteTransaction transaction = connection.BeginTransaction();
        foreach ((string messageId, string sourceHash) in updates)
        {
            using SqliteCommand updateCommand = connection.CreateCommand();
            updateCommand.Transaction = transaction;
            updateCommand.CommandText = """
                UPDATE gmail_report_history
                SET source_hash = $sourceHash
                WHERE gmail_message_id = $messageId
                  AND (source_hash IS NULL OR TRIM(source_hash) = '');
                """;
            updateCommand.Parameters.AddWithValue("$sourceHash", sourceHash);
            updateCommand.Parameters.AddWithValue("$messageId", messageId);
            updateCommand.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private static string TryReadZipFileName(string zipUrl)
    {
        return Uri.TryCreate(zipUrl, UriKind.Absolute, out Uri? uri)
            ? Path.GetFileName(uri.AbsolutePath)
            : string.Empty;
    }
    public DateTime? ReadLatestSuccessfulImportAtUtc()
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT last_attempt_at_utc
            FROM gmail_report_history
            WHERE import_status = 'ok'
            ORDER BY last_attempt_at_utc DESC
            LIMIT 1;
            """;
        object? value = command.ExecuteScalar();
        if (value is not string timestamp || string.IsNullOrWhiteSpace(timestamp))
        {
            return null;
        }

        return DateTime.Parse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
    }

    public void UpsertHistory(GmailReportHistoryRow row)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO gmail_report_history (
                gmail_message_id,
                gmail_internal_date_utc,
                sender,
                subject,
                zip_url,
                source_hash,
                download_status,
                import_status,
                archived,
                error_text,
                first_seen_at_utc,
                last_attempt_at_utc
            )
            VALUES (
                $gmailMessageId,
                $gmailInternalDateUtc,
                $sender,
                $subject,
                $zipUrl,
                $sourceHash,
                $downloadStatus,
                $importStatus,
                $archived,
                $errorText,
                $firstSeenAtUtc,
                $lastAttemptAtUtc
            )
            ON CONFLICT(gmail_message_id) DO UPDATE SET
                gmail_internal_date_utc = excluded.gmail_internal_date_utc,
                sender = excluded.sender,
                subject = excluded.subject,
                zip_url = excluded.zip_url,
                source_hash = COALESCE(excluded.source_hash, gmail_report_history.source_hash),
                download_status = excluded.download_status,
                import_status = excluded.import_status,
                archived = excluded.archived,
                error_text = excluded.error_text,
                first_seen_at_utc = excluded.first_seen_at_utc,
                last_attempt_at_utc = excluded.last_attempt_at_utc;
            """;
        command.Parameters.AddWithValue("$gmailMessageId", row.GmailMessageId);
        command.Parameters.AddWithValue("$gmailInternalDateUtc", row.GmailInternalDate.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$sender", row.Sender);
        command.Parameters.AddWithValue("$subject", row.Subject);
        command.Parameters.AddWithValue("$zipUrl", row.ZipUrl);
        command.Parameters.AddWithValue("$sourceHash", (object?)row.SourceHash ?? DBNull.Value);
        command.Parameters.AddWithValue("$downloadStatus", row.DownloadStatus);
        command.Parameters.AddWithValue("$importStatus", row.ImportStatus);
        command.Parameters.AddWithValue("$archived", row.Archived ? 1 : 0);
        command.Parameters.AddWithValue("$errorText", (object?)row.ErrorText ?? DBNull.Value);
        command.Parameters.AddWithValue("$firstSeenAtUtc", row.FirstSeenAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$lastAttemptAtUtc", row.LastAttemptAtUtc.ToString("O"));
        command.ExecuteNonQuery();
    }

    public bool HasSuccessfulMessage(string gmailMessageId, string zipUrl)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM gmail_report_history
                WHERE import_status = 'ok'
                  AND (gmail_message_id = $gmailMessageId OR zip_url = $zipUrl)
                LIMIT 1
            );
            """;
        command.Parameters.AddWithValue("$gmailMessageId", gmailMessageId);
        command.Parameters.AddWithValue("$zipUrl", zipUrl);
        return Convert.ToInt32(command.ExecuteScalar() ?? 0) == 1;
    }

    public bool HasSuccessfulMessageId(string gmailMessageId)
    {
        return HistoryExists("gmail_message_id = $value AND import_status = 'ok'", gmailMessageId);
    }

    public bool HasSuccessfulZipUrl(string zipUrl)
    {
        return HistoryExists("zip_url = $value AND import_status = 'ok'", zipUrl);
    }

    public bool WasMessagePermanentlyDeleted(string gmailMessageId)
    {
        return HistoryExists("gmail_message_id = $value AND archived = 1", gmailMessageId);
    }

    private bool HistoryExists(string predicate, string value)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT EXISTS (SELECT 1 FROM gmail_report_history WHERE {predicate} LIMIT 1);";
        command.Parameters.AddWithValue("$value", value);
        return Convert.ToInt32(command.ExecuteScalar() ?? 0) == 1;
    }
    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        return connection;
    }

    private static void EnsureColumnExists(SqliteConnection connection, string tableName, string columnName, string columnSqlDefinition)
    {
        using SqliteCommand pragmaCommand = connection.CreateCommand();
        pragmaCommand.CommandText = $"PRAGMA table_info({tableName});";
        using SqliteDataReader reader = pragmaCommand.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        using SqliteCommand alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnSqlDefinition};";
        alterCommand.ExecuteNonQuery();
    }

    private static GmailReportHistoryRow ReadHistoryRow(SqliteDataReader reader)
    {
        return new GmailReportHistoryRow(
            reader.GetString(0),
            DateTimeOffset.Parse(reader.GetString(1)),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetInt32(7) == 1,
            ReadNullableString(reader, 8),
            DateTime.Parse(reader.GetString(9)),
            DateTime.Parse(reader.GetString(10)),
            ReadNullableString(reader, 11));
    }

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTime? ReadNullableDateTime(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : DateTime.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }
}
