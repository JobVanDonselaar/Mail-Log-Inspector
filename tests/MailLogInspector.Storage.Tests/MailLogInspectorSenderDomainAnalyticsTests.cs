using MailLogInspector.Core;
using MailLogInspector.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class MailLogInspectorSenderDomainAnalyticsTests
{
    [Fact]
    public void Initialize_UpgradesSenderDomainAnalyticsSchemaWithDefaultsAndCompositeKeys()
    {
        string databasePath = CreateDatabasePath("schema");
        var store = new MailLogInspectorStore(databasePath);
        store.Initialize();

        using (var connection = Open(databasePath))
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                DROP TABLE analysis_daily_sender_domain;
                DROP TABLE analysis_daily_sender_reason;
                CREATE TABLE analysis_daily_sender_domain (
                    day_key INTEGER NOT NULL,
                    domain_id INTEGER NOT NULL,
                    total INTEGER NOT NULL,
                    delivered INTEGER NOT NULL,
                    underway INTEGER NOT NULL,
                    bounce INTEGER NOT NULL,
                    PRIMARY KEY(day_key, domain_id)
                ) WITHOUT ROWID;
                INSERT INTO analysis_daily_sender_domain
                    (day_key, domain_id, total, delivered, underway, bounce)
                VALUES (1, 2, 3, 1, 1, 1);
                """;
            command.ExecuteNonQuery();
        }

        store.Initialize();

        using var verifyConnection = Open(databasePath);
        IReadOnlyDictionary<string, (int NotNull, string? DefaultValue, int PrimaryKey)> senderColumns =
            ReadColumns(verifyConnection, "analysis_daily_sender_domain");
        foreach (string name in DurationColumnNames)
        {
            Assert.True(senderColumns.TryGetValue(name, out var column), $"Missing column {name}.");
            Assert.Equal(1, column.NotNull);
            Assert.Equal("0", column.DefaultValue);
        }

        using (SqliteCommand defaults = verifyConnection.CreateCommand())
        {
            defaults.CommandText = """
                SELECT duration_metrics_version, duration_count, duration_sum_seconds,
                       duration_missing_count, within_60_count, within_300_count,
                       within_900_count, within_3600_count
                FROM analysis_daily_sender_domain
                WHERE day_key = 1 AND domain_id = 2;
                """;
            using SqliteDataReader reader = defaults.ExecuteReader();
            Assert.True(reader.Read());
            for (int ordinal = 0; ordinal < reader.FieldCount; ordinal++)
            {
                Assert.Equal(0L, reader.GetInt64(ordinal));
            }
        }

        IReadOnlyDictionary<string, (int NotNull, string? DefaultValue, int PrimaryKey)> reasonColumns =
            ReadColumns(verifyConnection, "analysis_daily_sender_reason");
        Assert.Equal(1, reasonColumns["day_key"].PrimaryKey);
        Assert.Equal(2, reasonColumns["domain_id"].PrimaryKey);
        Assert.Equal(3, reasonColumns["reason_code"].PrimaryKey);
        Assert.Equal(0, reasonColumns["total"].PrimaryKey);
        Assert.Contains("WITHOUT ROWID", ReadCreateTableSql(verifyConnection, "analysis_daily_sender_reason"), StringComparison.OrdinalIgnoreCase);

        IReadOnlyDictionary<string, (int NotNull, string? DefaultValue, int PrimaryKey)> metadataColumns =
            ReadColumns(verifyConnection, "analysis_metadata");
        Assert.Equal(1, metadataColumns["metadata_key"].PrimaryKey);
        Assert.Equal(1, metadataColumns["metadata_value"].NotNull);
        Assert.Contains("WITHOUT ROWID", ReadCreateTableSql(verifyConnection, "analysis_metadata"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RebuildAnalysisData_AggregatesExactSenderDurationBucketsAndBounceReasons()
    {
        (MailLogInspectorStore store, string databasePath, DateTime accepted) = CreatePreparedStore("aggregate");

        store.RebuildAnalysisData();

        AssertSenderAggregate(databasePath, accepted, expectedVersion: 1);
        AssertSenderReasons(databasePath, accepted);
    }

    [Fact]
    public void EnsureSenderDomainAnalyticsAggregates_RebuildsStaleRowsOnlyOnce()
    {
        (MailLogInspectorStore store, string databasePath, DateTime accepted) = CreatePreparedStore("backfill");
        MarkSenderAnalyticsStale(databasePath);

        Assert.True(store.EnsureSenderDomainAnalyticsAggregates());
        Assert.False(store.EnsureSenderDomainAnalyticsAggregates());
        AssertSenderAggregate(databasePath, accepted, expectedVersion: 1);
        AssertSenderReasons(databasePath, accepted);
    }

    [Fact]
    public void ImportsMaintainSenderDomainAnalyticsVersionMarker()
    {
        string databasePath = CreateDatabasePath("marker");
        var store = new MailLogInspectorStore(databasePath);
        store.Initialize();
        DateTime accepted = new(2026, 7, 10, 8, 0, 0);

        store.SaveImport("current.csv", "current-hash", null, new[] { Entry(1, accepted) }, 0);

        Assert.Equal(2, ReadSenderAnalyticsVersion(databasePath));
        Assert.False(store.EnsureSenderDomainAnalyticsAggregates());

        store.SaveImport(
            "deferred.csv",
            "deferred-hash",
            null,
            new[] { Entry(2, accepted.AddMinutes(1)) },
            0,
            rebuildAnalysis: false);

        Assert.Equal(0, ReadSenderAnalyticsVersion(databasePath));
        Assert.True(store.EnsureSenderDomainAnalyticsAggregates());
        Assert.Equal(2, ReadSenderAnalyticsVersion(databasePath));
        Assert.False(store.EnsureSenderDomainAnalyticsAggregates());
        Assert.Equal(2, ReadSenderAggregateTotal(databasePath));
    }

    [Fact]
    public void EnsureSenderDomainAnalyticsAggregates_HealthyMarkerDoesNotReconcileDetailRows()
    {
        string databasePath = CreateDatabasePath("constant-cost");
        var store = new MailLogInspectorStore(databasePath);
        store.Initialize();
        store.SaveImport(
            "constant-cost.csv",
            "constant-cost-hash",
            null,
            new[] { Entry(1, new DateTime(2026, 7, 10, 8, 0, 0)) },
            0);

        using (var connection = Open(databasePath))
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE mail_items SET status = 3, reason_code = 2;";
            command.ExecuteNonQuery();
        }

        Assert.Equal(2, ReadSenderAnalyticsVersion(databasePath));
        Assert.False(store.EnsureSenderDomainAnalyticsAggregates());
    }

    [Fact]
    public void EnsureSenderDomainAnalyticsAggregates_RollsBackAndRetriesAfterInsertFailure()
    {
        (MailLogInspectorStore store, string databasePath, DateTime accepted) = CreatePreparedStore("rollback");
        MarkSenderAnalyticsStale(databasePath);
        using (var connection = Open(databasePath))
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO analysis_daily_sender_reason (day_key, domain_id, reason_code, total)
                VALUES (99, 99, 99, 99);
                CREATE TRIGGER fail_sender_reason_backfill
                BEFORE INSERT ON analysis_daily_sender_reason
                BEGIN
                    SELECT RAISE(ABORT, 'injected sender reason failure');
                END;
                """;
            command.ExecuteNonQuery();
        }

        Assert.Throws<SqliteException>(() => store.EnsureSenderDomainAnalyticsAggregates());

        Assert.Equal(0, ReadSenderAnalyticsVersion(databasePath));

        using (var connection = Open(databasePath))
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT
                    (SELECT COUNT(*) FROM analysis_daily_sender_domain WHERE duration_metrics_version = 0),
                    (SELECT COUNT(*) FROM analysis_daily_sender_reason WHERE day_key = 99 AND domain_id = 99 AND reason_code = 99);
                """;
            using SqliteDataReader reader = command.ExecuteReader();
            Assert.True(reader.Read());
            Assert.True(reader.GetInt32(0) > 0);
            Assert.Equal(1, reader.GetInt32(1));
        }

        using (var connection = Open(databasePath))
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "DROP TRIGGER fail_sender_reason_backfill;";
            command.ExecuteNonQuery();
        }

        Assert.True(store.EnsureSenderDomainAnalyticsAggregates());
        Assert.Equal(2, ReadSenderAnalyticsVersion(databasePath));
        Assert.False(store.EnsureSenderDomainAnalyticsAggregates());
        AssertSenderAggregate(databasePath, accepted, expectedVersion: 1);
        AssertSenderReasons(databasePath, accepted);
    }

    private static readonly string[] DurationColumnNames =
    {
        "duration_metrics_version",
        "duration_count",
        "duration_sum_seconds",
        "duration_missing_count",
        "within_60_count",
        "within_300_count",
        "within_900_count",
        "within_3600_count"
    };

    private static (MailLogInspectorStore Store, string DatabasePath, DateTime Accepted) CreatePreparedStore(string suffix)
    {
        string databasePath = CreateDatabasePath(suffix);
        var store = new MailLogInspectorStore(databasePath);
        store.Initialize();
        DateTime accepted = new(2026, 7, 10, 8, 0, 0);
        store.SaveImport(
            suffix + ".csv",
            suffix + "-hash",
            null,
            Enumerable.Range(1, 10).Select(row => Entry(row, accepted.AddMinutes(row))),
            errorCount: 0);

        using var connection = Open(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            WITH ordered AS (
                SELECT mail_item_id, ROW_NUMBER() OVER (ORDER BY mail_item_id) AS row_number
                FROM mail_items
            )
            UPDATE mail_items
            SET status = CASE (SELECT row_number FROM ordered WHERE ordered.mail_item_id = mail_items.mail_item_id)
                    WHEN 1 THEN 3 WHEN 2 THEN 3 WHEN 3 THEN 3 WHEN 10 THEN 2 ELSE 1 END,
                duration_seconds = CASE (SELECT row_number FROM ordered WHERE ordered.mail_item_id = mail_items.mail_item_id)
                    WHEN 1 THEN 30 WHEN 2 THEN 120 WHEN 3 THEN NULL WHEN 4 THEN NULL
                    WHEN 5 THEN 60 WHEN 6 THEN 300 WHEN 7 THEN 900 WHEN 8 THEN 3600
                    WHEN 9 THEN 3601 ELSE 10 END,
                reason_code = CASE (SELECT row_number FROM ordered WHERE ordered.mail_item_id = mail_items.mail_item_id)
                    WHEN 1 THEN 2 WHEN 2 THEN 2 WHEN 3 THEN 3 ELSE 2 END;
            """;
        command.ExecuteNonQuery();
        return (store, databasePath, accepted);
    }

    private static void MarkSenderAnalyticsStale(string databasePath)
    {
        using var connection = Open(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE analysis_daily_sender_domain
            SET duration_metrics_version = 0,
                duration_count = 0,
                duration_sum_seconds = 0,
                duration_missing_count = 0,
                within_60_count = 0,
                within_300_count = 0,
                within_900_count = 0,
                within_3600_count = 0;
            DELETE FROM analysis_daily_sender_reason;
            INSERT INTO analysis_metadata (metadata_key, metadata_value)
            VALUES ('sender_domain_analytics_version', 0)
            ON CONFLICT(metadata_key) DO UPDATE SET metadata_value = excluded.metadata_value;
            """;
        command.ExecuteNonQuery();
    }

    private static void AssertSenderAggregate(string databasePath, DateTime accepted, int expectedVersion)
    {
        using var connection = Open(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT total, delivered, underway, bounce, duration_metrics_version,
                   duration_count, duration_sum_seconds, duration_missing_count,
                   within_60_count, within_300_count, within_900_count, within_3600_count
            FROM analysis_daily_sender_domain
            WHERE day_key = $dayKey;
            """;
        command.Parameters.AddWithValue("$dayKey", accepted.Date.Ticks / TimeSpan.TicksPerDay);
        using SqliteDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(10, reader.GetInt32(0));
        Assert.Equal(6, reader.GetInt32(1));
        Assert.Equal(1, reader.GetInt32(2));
        Assert.Equal(3, reader.GetInt32(3));
        Assert.Equal(expectedVersion, reader.GetInt32(4));
        Assert.Equal(5, reader.GetInt32(5));
        Assert.Equal(8461L, reader.GetInt64(6));
        Assert.Equal(1, reader.GetInt32(7));
        Assert.Equal(1, reader.GetInt32(8));
        Assert.Equal(2, reader.GetInt32(9));
        Assert.Equal(3, reader.GetInt32(10));
        Assert.Equal(4, reader.GetInt32(11));
        Assert.False(reader.Read());
    }

    private static void AssertSenderReasons(string databasePath, DateTime accepted)
    {
        using var connection = Open(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT reason_code, total
            FROM analysis_daily_sender_reason
            WHERE day_key = $dayKey
            ORDER BY reason_code;
            """;
        command.Parameters.AddWithValue("$dayKey", accepted.Date.Ticks / TimeSpan.TicksPerDay);
        using SqliteDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(2, reader.GetInt32(0));
        Assert.Equal(2, reader.GetInt32(1));
        Assert.True(reader.Read());
        Assert.Equal(3, reader.GetInt32(0));
        Assert.Equal(1, reader.GetInt32(1));
        Assert.False(reader.Read());
    }

    private static IReadOnlyDictionary<string, (int NotNull, string? DefaultValue, int PrimaryKey)> ReadColumns(
        SqliteConnection connection,
        string tableName)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        using SqliteDataReader reader = command.ExecuteReader();
        var columns = new Dictionary<string, (int, string?, int)>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            columns.Add(reader.GetString(1), (reader.GetInt32(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetInt32(5)));
        }
        return columns;
    }

    private static string ReadCreateTableSql(SqliteConnection connection, string tableName)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        return (string)command.ExecuteScalar()!;
    }

    private static int ReadSenderAnalyticsVersion(string databasePath)
    {
        using var connection = Open(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE((
                SELECT metadata_value
                FROM analysis_metadata
                WHERE metadata_key = 'sender_domain_analytics_version'
            ), 0);
            """;
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static int ReadSenderAggregateTotal(string databasePath)
    {
        using var connection = Open(databasePath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(SUM(total), 0) FROM analysis_daily_sender_domain;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static SqliteConnection Open(string databasePath)
    {
        var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        return connection;
    }

    private static string CreateDatabasePath(string suffix)
    {
        string root = Path.Combine(Path.GetTempPath(), "mail-log-sender-domain-" + suffix + "-" + Guid.NewGuid().ToString("N"));
        return Path.Combine(root, "mail-log-inspector.sqlite");
    }

    private static SmtpLogEntry Entry(int row, DateTime accepted)
    {
        return new SmtpLogEntry(
            row,
            accepted,
            accepted.AddMinutes(1),
            "sender@example.com",
            "example.com",
            $"recipient-{row}@example.net",
            "example.net",
            "D",
            "250",
            "delivered",
            string.Empty,
            null,
            string.Empty,
            $"tracking-{row}",
            string.Empty);
    }
}
