using MailLogInspector.Core;
using Microsoft.Data.Sqlite;

namespace MailLogInspector.Storage;

internal static class MailLogInspectorLegacyDatabaseMigrator
{
    public static LegacyDatabaseMigrationResult Migrate(string sourceDatabasePath, string targetDatabasePath)
    {
        var sourceStore = new MailLogInspectorStore(sourceDatabasePath);
        if (sourceStore.GetDatabaseState() == MailLogInspectorDatabaseState.MissingOrEmpty)
        {
            throw new InvalidDataException($"Migratiebron bevat geen mail_items: {sourceDatabasePath}");
        }

        var targetStore = new MailLogInspectorStore(targetDatabasePath);
        targetStore.Initialize();

        using SqliteConnection connection = targetStore.OpenConnection();
        DropBulkLoadIndexes(connection);
        AttachSource(connection, sourceDatabasePath);
        try
        {
            string deliveredCount = LegacyImportColumnExists(connection, "delivered_count")
                ? "i.delivered_count"
                : "(SELECT COUNT(*) FROM legacy.mail_items mi WHERE mi.last_import_id = i.import_id AND mi.status = 1)";
            string bounceCount = LegacyImportColumnExists(connection, "bounce_count")
                ? "i.bounce_count"
                : "(SELECT COUNT(*) FROM legacy.mail_items mi WHERE mi.last_import_id = i.import_id AND mi.status = 3)";
            string underwayCount = LegacyImportColumnExists(connection, "underway_count")
                ? "i.underway_count"
                : "(SELECT COUNT(*) FROM legacy.mail_items mi WHERE mi.last_import_id = i.import_id AND mi.status = 2)";
            using SqliteTransaction transaction = connection.BeginTransaction();
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                INSERT INTO mail_domains (domain_id, domain_name)
                SELECT domain_id, domain_name FROM legacy.mail_domains;

                INSERT INTO mail_addresses (address_id, local_part, domain_id)
                SELECT address_id, local_part, domain_id FROM legacy.mail_addresses;

                INSERT INTO imports (
                    import_id, source_path, source_file_name, source_hash, imported_at,
                    report_start, report_end, row_count, delivered_count, bounce_count,
                    underway_count, archive_path)
                SELECT i.import_id, i.source_path, i.source_file_name, i.source_hash, i.imported_at,
                       i.report_start, i.report_end, i.row_count, {deliveredCount}, {bounceCount},
                       {underwayCount}, i.archive_path
                FROM legacy.imports i;

                INSERT INTO mail_items (
                    tracking_key, recipient_address_id, recipient_domain_id, sender_address_id,
                    sender_domain_id, accepted_at, status, last_seen_at, duration_seconds,
                    response_code, reason_code, last_import_id)
                SELECT tracking_key, recipient_address_id, recipient_domain_id, sender_address_id,
                       sender_domain_id, accepted_at, status, last_seen_at, duration_seconds,
                       response_code, reason_code, last_import_id
                FROM legacy.mail_items;

                INSERT INTO import_reason_counts (import_id, reason_code, total)
                SELECT last_import_id, reason_code, COUNT(*)
                FROM mail_items
                WHERE status = 3
                GROUP BY last_import_id, reason_code;
                """;
            command.ExecuteNonQuery();
            transaction.Commit();
        }
        finally
        {
            using SqliteCommand detach = connection.CreateCommand();
            detach.CommandText = "DETACH DATABASE legacy;";
            detach.ExecuteNonQuery();
        }

        targetStore.Initialize();
        targetStore.RebuildAnalysisData();
        targetStore.OptimizeForReadPerformance();
        return ReadCounts(targetDatabasePath);
    }

    public static LegacyDatabaseMigrationResult ReadCounts(string databasePath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            DefaultTimeout = 30
        }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT COUNT(*) FROM mail_items), (SELECT COUNT(*) FROM imports);";
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidDataException($"Database-aantallen konden niet worden gelezen: {databasePath}");
        }

        return new LegacyDatabaseMigrationResult(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)));
    }

    private static void AttachSource(SqliteConnection connection, string sourceDatabasePath)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "ATTACH DATABASE $sourcePath AS legacy;";
        command.Parameters.AddWithValue("$sourcePath", Path.GetFullPath(sourceDatabasePath));
        command.ExecuteNonQuery();
    }

    private static bool LegacyImportColumnExists(SqliteConnection connection, string columnName)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA legacy.table_info(imports);";
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

    private static void DropBulkLoadIndexes(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            DROP INDEX IF EXISTS ix_mail_log_inspector_items_accepted;
            DROP INDEX IF EXISTS ix_mail_log_inspector_items_sender_address_accepted_at;
            DROP INDEX IF EXISTS ix_mail_log_inspector_items_recipient_address_accepted_at;
            DROP INDEX IF EXISTS ix_mail_log_inspector_items_sender_domain_accepted_at;
            DROP INDEX IF EXISTS ix_mail_log_inspector_items_recipient_domain_accepted_at;
            """;
        command.ExecuteNonQuery();
    }
}

internal sealed record LegacyDatabaseMigrationResult(int RowCount, int ImportCount);
