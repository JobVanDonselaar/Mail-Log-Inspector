using System.Globalization;
using Microsoft.Data.Sqlite;

namespace MailLogInspector.Storage;

/// <summary>
/// Beheert de instellingen voor bouncemeldingen in de operationele database:
/// algemene instellingen plus een aan/uit-regel per afzender-e-mailadres.
/// </summary>
public sealed class BounceNotificationOperationalStore
{
    private readonly string _databasePath;

    public BounceNotificationOperationalStore(string databasePath)
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
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS bounce_notification_settings (
                settings_id INTEGER PRIMARY KEY CHECK (settings_id = 1),
                enabled INTEGER NOT NULL DEFAULT 0,
                auto_send_after_import INTEGER NOT NULL DEFAULT 0,
                transport TEXT NOT NULL DEFAULT 'gmail',
                from_address TEXT NULL,
                from_display_name TEXT NULL,
                subject_template TEXT NULL,
                relay_host TEXT NULL,
                relay_port INTEGER NOT NULL DEFAULT 587,
                relay_username TEXT NULL,
                encrypted_relay_password TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS bounce_notification_senders (
                sender_address TEXT PRIMARY KEY,
                enabled INTEGER NOT NULL DEFAULT 0,
                never_notify INTEGER NOT NULL DEFAULT 0,
                recipient_override TEXT NULL,
                last_notified_at_utc TEXT NULL,
                last_notified_bounce_count INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS bounce_notification_log (
                log_id INTEGER PRIMARY KEY AUTOINCREMENT,
                sent_at_utc TEXT NOT NULL,
                sender_address TEXT NOT NULL,
                recipient TEXT NOT NULL,
                bounce_count INTEGER NOT NULL DEFAULT 0,
                scope TEXT NOT NULL DEFAULT 'import',
                import_id INTEGER NULL,
                period_start TEXT NULL,
                period_end TEXT NULL,
                source_file TEXT NULL,
                success INTEGER NOT NULL DEFAULT 1,
                error_message TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_bounce_notification_log_sent
                ON bounce_notification_log (sent_at_utc DESC);

            CREATE INDEX IF NOT EXISTS ix_bounce_notification_log_sender
                ON bounce_notification_log (sender_address, period_start, period_end);

            INSERT OR IGNORE INTO bounce_notification_settings (settings_id) VALUES (1);
            """;
        command.ExecuteNonQuery();

        EnsureContentColumns(connection);
        EnsureSenderColumns(connection);
    }

    /// <summary>
    /// Voegt de inhoudskolommen toe aan databases die van voor de instelbare mailinhoud stammen.
    /// </summary>
    private static void EnsureContentColumns(SqliteConnection connection)
    {
        HashSet<string> existing = ReadColumnNames(connection, "bounce_notification_settings");

        (string Column, string Definition)[] contentColumns =
        [
            ("include_excel_attachment", "INTEGER NOT NULL DEFAULT 1"),
            ("include_kpi_summary", "INTEGER NOT NULL DEFAULT 1"),
            ("include_reason_breakdown", "INTEGER NOT NULL DEFAULT 1"),
            ("include_recipient_domain_breakdown", "INTEGER NOT NULL DEFAULT 1"),
            ("include_detail_table", "INTEGER NOT NULL DEFAULT 1"),
            ("include_source_file", "INTEGER NOT NULL DEFAULT 1"),
            ("max_detail_rows", "INTEGER NOT NULL DEFAULT 100"),
            ("body_format", "TEXT NOT NULL DEFAULT 'both'"),
            ("intro_text", "TEXT NULL"),
            ("footer_text", "TEXT NULL")
        ];

        foreach ((string column, string definition) in contentColumns)
        {
            if (existing.Contains(column))
            {
                continue;
            }

            using SqliteCommand alter = connection.CreateCommand();
            alter.CommandText =
                $"ALTER TABLE bounce_notification_settings ADD COLUMN {column} {definition};";
            alter.ExecuteNonQuery();
        }
    }

    private static void EnsureSenderColumns(SqliteConnection connection)
    {
        HashSet<string> existing = ReadColumnNames(connection, "bounce_notification_senders");
        if (existing.Contains("never_notify"))
        {
            return;
        }

        using SqliteCommand alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE bounce_notification_senders ADD COLUMN never_notify INTEGER NOT NULL DEFAULT 0;";
        alter.ExecuteNonQuery();
    }

    private static HashSet<string> ReadColumnNames(SqliteConnection connection, string tableName)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        using SqliteDataReader reader = command.ExecuteReader();

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    public BounceNotificationSettings LoadSettings()
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT enabled,
                   auto_send_after_import,
                   transport,
                   from_address,
                   from_display_name,
                   subject_template,
                   relay_host,
                   relay_port,
                   relay_username,
                   encrypted_relay_password,
                   include_excel_attachment,
                   include_kpi_summary,
                   include_reason_breakdown,
                   include_recipient_domain_breakdown,
                   include_detail_table,
                   include_source_file,
                   max_detail_rows,
                   body_format,
                   intro_text,
                   footer_text
            FROM bounce_notification_settings
            WHERE settings_id = 1;
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return BounceNotificationSettings.Default;
        }

        return new BounceNotificationSettings(
            Enabled: reader.GetInt64(0) != 0,
            AutoSendAfterImport: reader.GetInt64(1) != 0,
            Transport: BounceNotificationTransport.Normalize(ReadNullableString(reader, 2)),
            FromAddress: ReadNullableString(reader, 3),
            FromDisplayName: ReadNullableString(reader, 4),
            SubjectTemplate: ReadNullableString(reader, 5),
            RelayHost: ReadNullableString(reader, 6),
            RelayPort: reader.IsDBNull(7) ? 587 : (int)reader.GetInt64(7),
            RelayUsername: ReadNullableString(reader, 8),
            EncryptedRelayPassword: ReadNullableString(reader, 9),
            Content: new BounceNotificationContentOptions(
                IncludeExcelAttachment: ReadBoolean(reader, 10, defaultValue: true),
                IncludeKpiSummary: ReadBoolean(reader, 11, defaultValue: true),
                IncludeReasonBreakdown: ReadBoolean(reader, 12, defaultValue: true),
                IncludeRecipientDomainBreakdown: ReadBoolean(reader, 13, defaultValue: true),
                IncludeDetailTable: ReadBoolean(reader, 14, defaultValue: true),
                IncludeSourceFileName: ReadBoolean(reader, 15, defaultValue: true),
                MaxDetailRows: reader.IsDBNull(16)
                    ? BounceNotificationContentOptions.DefaultMaxDetailRows
                    : (int)reader.GetInt64(16),
                BodyFormat: BounceNotificationBodyFormat.Normalize(ReadNullableString(reader, 17)),
                IntroText: ReadNullableString(reader, 18) ?? BounceNotificationContentOptions.DefaultIntroText,
                FooterText: ReadNullableString(reader, 19) ?? BounceNotificationContentOptions.DefaultFooterText));
    }

    public void SaveSettings(BounceNotificationSettings settings)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO bounce_notification_settings (
                settings_id,
                enabled,
                auto_send_after_import,
                transport,
                from_address,
                from_display_name,
                subject_template,
                relay_host,
                relay_port,
                relay_username,
                encrypted_relay_password,
                include_excel_attachment,
                include_kpi_summary,
                include_reason_breakdown,
                include_recipient_domain_breakdown,
                include_detail_table,
                include_source_file,
                max_detail_rows,
                body_format,
                intro_text,
                footer_text
            )
            VALUES (1, $enabled, $autoSend, $transport, $fromAddress, $fromDisplayName,
                    $subjectTemplate, $relayHost, $relayPort, $relayUsername, $relayPassword,
                    $includeAttachment, $includeKpi, $includeReasons, $includeDomains,
                    $includeDetails, $includeSource, $maxDetailRows, $bodyFormat,
                    $introText, $footerText)
            ON CONFLICT(settings_id) DO UPDATE SET
                enabled = excluded.enabled,
                auto_send_after_import = excluded.auto_send_after_import,
                transport = excluded.transport,
                from_address = excluded.from_address,
                from_display_name = excluded.from_display_name,
                subject_template = excluded.subject_template,
                relay_host = excluded.relay_host,
                relay_port = excluded.relay_port,
                relay_username = excluded.relay_username,
                encrypted_relay_password = excluded.encrypted_relay_password,
                include_excel_attachment = excluded.include_excel_attachment,
                include_kpi_summary = excluded.include_kpi_summary,
                include_reason_breakdown = excluded.include_reason_breakdown,
                include_recipient_domain_breakdown = excluded.include_recipient_domain_breakdown,
                include_detail_table = excluded.include_detail_table,
                include_source_file = excluded.include_source_file,
                max_detail_rows = excluded.max_detail_rows,
                body_format = excluded.body_format,
                intro_text = excluded.intro_text,
                footer_text = excluded.footer_text;
            """;
        command.Parameters.AddWithValue("$enabled", settings.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$autoSend", settings.AutoSendAfterImport ? 1 : 0);
        command.Parameters.AddWithValue("$transport", BounceNotificationTransport.Normalize(settings.Transport));
        command.Parameters.AddWithValue("$fromAddress", ToDbValue(settings.FromAddress));
        command.Parameters.AddWithValue("$fromDisplayName", ToDbValue(settings.FromDisplayName));
        command.Parameters.AddWithValue("$subjectTemplate", ToDbValue(settings.SubjectTemplate));
        command.Parameters.AddWithValue("$relayHost", ToDbValue(settings.RelayHost));
        command.Parameters.AddWithValue("$relayPort", settings.RelayPort <= 0 ? 587 : settings.RelayPort);
        command.Parameters.AddWithValue("$relayUsername", ToDbValue(settings.RelayUsername));
        command.Parameters.AddWithValue("$relayPassword", ToDbValue(settings.EncryptedRelayPassword));

        BounceNotificationContentOptions content = settings.Content ?? BounceNotificationContentOptions.Default;
        command.Parameters.AddWithValue("$includeAttachment", content.IncludeExcelAttachment ? 1 : 0);
        command.Parameters.AddWithValue("$includeKpi", content.IncludeKpiSummary ? 1 : 0);
        command.Parameters.AddWithValue("$includeReasons", content.IncludeReasonBreakdown ? 1 : 0);
        command.Parameters.AddWithValue("$includeDomains", content.IncludeRecipientDomainBreakdown ? 1 : 0);
        command.Parameters.AddWithValue("$includeDetails", content.IncludeDetailTable ? 1 : 0);
        command.Parameters.AddWithValue("$includeSource", content.IncludeSourceFileName ? 1 : 0);
        command.Parameters.AddWithValue("$maxDetailRows", content.ResolveMaxDetailRows());
        command.Parameters.AddWithValue("$bodyFormat", content.ResolveBodyFormat());
        command.Parameters.AddWithValue("$introText", ToBodyTextDbValue(content.IntroText));
        command.Parameters.AddWithValue("$footerText", ToBodyTextDbValue(content.FooterText));
        command.ExecuteNonQuery();
    }

    /// <summary>Alle opgeslagen afzenderregels, gesorteerd op adres.</summary>
    public IReadOnlyList<BounceNotificationSender> LoadSenders()
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT sender_address,
                   enabled,
                   never_notify,
                   recipient_override,
                   last_notified_at_utc,
                   last_notified_bounce_count
            FROM bounce_notification_senders
            ORDER BY sender_address;
            """;
        using SqliteDataReader reader = command.ExecuteReader();

        List<BounceNotificationSender> senders = [];
        while (reader.Read())
        {
            senders.Add(new BounceNotificationSender(
                reader.GetString(0),
                reader.GetInt64(1) != 0,
                reader.GetInt64(2) != 0,
                ReadNullableString(reader, 3),
                ReadNullableDateTime(reader, 4),
                reader.IsDBNull(5) ? 0 : (int)reader.GetInt64(5)));
        }

        return senders;
    }

    public BounceNotificationSender LoadSender(string senderAddress)
    {
        string normalized = NormalizeAddress(senderAddress);
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT sender_address,
                   enabled,
                   never_notify,
                   recipient_override,
                   last_notified_at_utc,
                   last_notified_bounce_count
            FROM bounce_notification_senders
            WHERE sender_address = $senderAddress;
            """;
        command.Parameters.AddWithValue("$senderAddress", normalized);
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? new BounceNotificationSender(
                reader.GetString(0),
                reader.GetInt64(1) != 0,
                reader.GetInt64(2) != 0,
                ReadNullableString(reader, 3),
                ReadNullableDateTime(reader, 4),
                reader.IsDBNull(5) ? 0 : (int)reader.GetInt64(5))
            : BounceNotificationSender.CreateDisabled(normalized);
    }

    public void SaveSender(BounceNotificationSender sender)
    {
        using SqliteConnection connection = OpenConnection();
        SaveSender(connection, sender);
    }

    /// <summary>Slaat meerdere afzenderregels op in één transactie.</summary>
    public void SaveSenders(IEnumerable<BounceNotificationSender> senders)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();
        foreach (BounceNotificationSender sender in senders)
        {
            SaveSender(connection, sender, transaction);
        }

        transaction.Commit();
    }

    /// <summary>
    /// Zorgt dat elk aangetroffen afzenderadres een regel heeft. Nieuwe afzenders staan altijd uit;
    /// bestaande regels blijven ongewijzigd.
    /// </summary>
    public void EnsureSendersExist(IEnumerable<string> senderAddresses)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();
        foreach (string address in senderAddresses)
        {
            string normalized = NormalizeAddress(address);
            if (normalized.Length == 0)
            {
                continue;
            }

            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO bounce_notification_senders (sender_address, enabled)
                VALUES ($senderAddress, 0);
                """;
            command.Parameters.AddWithValue("$senderAddress", normalized);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>Zet alle bekende afzenders in één keer aan of uit, behalve regels die op nooit staan.</summary>
    public void SetAllSendersEnabled(bool enabled)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE bounce_notification_senders
            SET enabled = $enabled
            WHERE never_notify = 0;
            """;
        command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        command.ExecuteNonQuery();
    }

    public void RecordNotification(string senderAddress, DateTime sentAtUtc, int bounceCount)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO bounce_notification_senders (
                sender_address, enabled, never_notify, last_notified_at_utc, last_notified_bounce_count)
            VALUES ($senderAddress, 1, 0, $sentAtUtc, $bounceCount)
            ON CONFLICT(sender_address) DO UPDATE SET
                last_notified_at_utc = excluded.last_notified_at_utc,
                last_notified_bounce_count = excluded.last_notified_bounce_count;
            """;
        command.Parameters.AddWithValue("$senderAddress", NormalizeAddress(senderAddress));
        command.Parameters.AddWithValue("$sentAtUtc", NormalizeUtc(sentAtUtc).ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$bounceCount", bounceCount);
        command.ExecuteNonQuery();
    }

    /// <summary>Legt één verzendpoging vast, geslaagd of mislukt, zodat terug te zien is wat al gemaild is.</summary>
    public long AppendLogEntry(
        string senderAddress,
        string recipient,
        int bounceCount,
        BounceNotificationPeriod period,
        bool success,
        string? errorMessage,
        DateTime? sentAtUtc = null)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO bounce_notification_log (
                sent_at_utc, sender_address, recipient, bounce_count,
                scope, import_id, period_start, period_end, source_file, success, error_message)
            VALUES ($sentAtUtc, $senderAddress, $recipient, $bounceCount,
                $scope, $importId, $periodStart, $periodEnd, $sourceFile, $success, $errorMessage);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue(
            "$sentAtUtc",
            NormalizeUtc(sentAtUtc ?? DateTime.UtcNow).ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$senderAddress", NormalizeAddress(senderAddress));
        command.Parameters.AddWithValue("$recipient", NormalizeAddress(recipient));
        command.Parameters.AddWithValue("$bounceCount", bounceCount);
        command.Parameters.AddWithValue("$scope", BounceNotificationScope.Normalize(period.Scope));
        command.Parameters.AddWithValue("$importId", period.ImportId.HasValue ? period.ImportId.Value : DBNull.Value);
        command.Parameters.AddWithValue("$periodStart", ToDateDbValue(period.FromInclusive));
        command.Parameters.AddWithValue("$periodEnd", ToDateDbValue(period.ThroughInclusive));
        command.Parameters.AddWithValue("$sourceFile", ToDbValue(period.SourceFileName));
        command.Parameters.AddWithValue("$success", success ? 1 : 0);
        command.Parameters.AddWithValue("$errorMessage", ToDbValue(errorMessage));

        object? result = command.ExecuteScalar();
        return result is null or DBNull ? 0L : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    /// <summary>De meest recente verzendpogingen, nieuwste eerst.</summary>
    public IReadOnlyList<BounceNotificationLogEntry> ReadLogEntries(int maxRows = 250)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT log_id, sent_at_utc, sender_address, recipient, bounce_count,
                   scope, import_id, period_start, period_end, source_file, success, error_message
            FROM bounce_notification_log
            ORDER BY sent_at_utc DESC, log_id DESC
            LIMIT $maxRows;
            """;
        command.Parameters.AddWithValue("$maxRows", maxRows <= 0 ? 250 : maxRows);

        List<BounceNotificationLogEntry> entries = [];
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new BounceNotificationLogEntry(
                reader.GetInt64(0),
                ReadNullableDateTime(reader, 1) ?? DateTime.UtcNow,
                reader.GetString(2),
                reader.GetString(3),
                (int)reader.GetInt64(4),
                BounceNotificationScope.Normalize(ReadNullableString(reader, 5)),
                reader.IsDBNull(6) ? null : reader.GetInt64(6),
                ReadNullableDateTime(reader, 7),
                ReadNullableDateTime(reader, 8),
                ReadNullableString(reader, 9),
                ReadBoolean(reader, 10, defaultValue: true),
                ReadNullableString(reader, 11)));
        }

        return entries;
    }

    /// <summary>
    /// Wanneer een afzender voor het laatst een geslaagde melding over deze periode kreeg.
    /// Hiermee is te zien of een periode al gemaild is voordat er opnieuw verstuurd wordt.
    /// </summary>
    public IReadOnlyDictionary<string, DateTime> ReadSuccessfulSendsForPeriod(
        DateTime fromInclusive,
        DateTime throughInclusive)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT sender_address, MAX(sent_at_utc)
            FROM bounce_notification_log
            WHERE success = 1
              AND period_start = $periodStart
              AND period_end = $periodEnd
            GROUP BY sender_address;
            """;
        command.Parameters.AddWithValue("$periodStart", ToDateDbValue(fromInclusive));
        command.Parameters.AddWithValue("$periodEnd", ToDateDbValue(throughInclusive));

        Dictionary<string, DateTime> sends = new(StringComparer.OrdinalIgnoreCase);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            DateTime? sentAt = ReadNullableDateTime(reader, 1);
            if (sentAt.HasValue)
            {
                sends[reader.GetString(0)] = sentAt.Value;
            }
        }

        return sends;
    }

    private static void SaveSender(
        SqliteConnection connection,
        BounceNotificationSender sender,
        SqliteTransaction? transaction = null)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO bounce_notification_senders (
                sender_address,
                enabled,
                never_notify,
                recipient_override,
                last_notified_at_utc,
                last_notified_bounce_count
            )
            VALUES ($senderAddress, $enabled, $neverNotify, $recipientOverride, $lastNotifiedAtUtc, $bounceCount)
            ON CONFLICT(sender_address) DO UPDATE SET
                enabled = excluded.enabled,
                never_notify = excluded.never_notify,
                recipient_override = excluded.recipient_override,
                last_notified_at_utc = excluded.last_notified_at_utc,
                last_notified_bounce_count = excluded.last_notified_bounce_count;
            """;
        command.Parameters.AddWithValue("$senderAddress", NormalizeAddress(sender.SenderAddress));
        command.Parameters.AddWithValue("$enabled", sender.NeverNotify ? 0 : (sender.Enabled ? 1 : 0));
        command.Parameters.AddWithValue("$neverNotify", sender.NeverNotify ? 1 : 0);
        command.Parameters.AddWithValue("$recipientOverride", ToDbValue(sender.RecipientOverride));
        command.Parameters.AddWithValue("$lastNotifiedAtUtc", ToDbValue(sender.LastNotifiedAtUtc));
        command.Parameters.AddWithValue("$bounceCount", sender.LastNotifiedBounceCount);
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        return connection;
    }

    private static string NormalizeAddress(string? address)
    {
        return string.IsNullOrWhiteSpace(address)
            ? string.Empty
            : address.Trim().ToLowerInvariant();
    }

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static bool ReadBoolean(SqliteDataReader reader, int ordinal, bool defaultValue)
    {
        return reader.IsDBNull(ordinal) ? defaultValue : reader.GetInt64(ordinal) != 0;
    }

    private static DateTime? ReadNullableDateTime(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : DateTime.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static object ToDbValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    }

    /// <summary>
    /// Inleiding en afsluiting kennen een standaardtekst. Daarom moet "nooit ingesteld" te
    /// onderscheiden blijven van "bewust leeggemaakt": het eerste wordt NULL en valt terug op de
    /// standaard, het tweede wordt een lege tekst en blijft leeg.
    /// </summary>
    private static object ToBodyTextDbValue(string? value)
    {
        return value is null ? DBNull.Value : value.Trim();
    }

    private static object ToDbValue(DateTime? value)
    {
        return value.HasValue
            ? NormalizeUtc(value.Value).ToString("O", CultureInfo.InvariantCulture)
            : DBNull.Value;
    }

    /// <summary>Periodegrenzen worden als kale datum opgeslagen zodat ze exact vergelijkbaar blijven.</summary>
    private static object ToDateDbValue(DateTime? value)
    {
        return value.HasValue
            ? value.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : DBNull.Value;
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
