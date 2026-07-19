using Microsoft.Data.Sqlite;

namespace MailLogInspector.Storage;

public sealed partial class MailLogInspectorStore
{
    private const string SenderDomainAnalyticsVersionKey = "sender_domain_analytics_version";
    private const int CurrentSenderDomainAnalyticsVersion = 2;

    public bool EnsureSenderDomainAnalyticsAggregates()
    {
        using SqliteConnection connection = OpenConnection();
        MailLogInspectorSchema.Ensure(connection);
        using SqliteTransaction transaction = connection.BeginTransaction();
        if (ReadSenderDomainAnalyticsVersion(connection, transaction) >= CurrentSenderDomainAnalyticsVersion)
        {
            transaction.Rollback();
            return false;
        }

        RebuildAnalysisTables(connection, transaction, CancellationToken.None);
        transaction.Commit();
        return true;
    }

    private static int ReadSenderDomainAnalyticsVersion(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COALESCE((
                SELECT metadata_value
                FROM analysis_metadata
                WHERE metadata_key = $metadataKey
            ), 0);
            """;
        command.Parameters.AddWithValue("$metadataKey", SenderDomainAnalyticsVersionKey);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void SetSenderDomainAnalyticsVersion(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int version)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO analysis_metadata (metadata_key, metadata_value)
            VALUES ($metadataKey, $metadataValue)
            ON CONFLICT(metadata_key) DO UPDATE SET metadata_value = excluded.metadata_value;
            """;
        command.Parameters.AddWithValue("$metadataKey", SenderDomainAnalyticsVersionKey);
        command.Parameters.AddWithValue("$metadataValue", version);
        command.ExecuteNonQuery();
    }

}