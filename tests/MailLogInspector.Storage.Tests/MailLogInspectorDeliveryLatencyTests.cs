using System.Collections;
using System.Reflection;
using MailLogInspector.Core;
using MailLogInspector.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class MailLogInspectorDeliveryLatencyTests
{
    [Fact]
    public void Initialize_AddsCompactDeliveryLatencyColumnsToDailyStatus()
    {
        string root = Path.Combine(Path.GetTempPath(), "mail-log-latency-schema-" + Guid.NewGuid().ToString("N"));
        string databasePath = Path.Combine(root, "mail-log-inspector.sqlite");
        var store = new MailLogInspectorStore(databasePath);

        store.Initialize();

        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(analysis_daily_status);";
        using SqliteDataReader reader = command.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        Assert.Contains("duration_metrics_version", columns);
        Assert.Contains("duration_count", columns);
        Assert.Contains("duration_sum_seconds", columns);
        Assert.Contains("duration_missing_count", columns);
        Assert.Contains("within_60_count", columns);
        Assert.Contains("within_300_count", columns);
        Assert.Contains("within_900_count", columns);
        Assert.Contains("within_3600_count", columns);
    }

    [Fact]
    public void RebuildAnalysisData_AggregatesDeliveredDurationBuckets()
    {
        string root = Path.Combine(Path.GetTempPath(), "mail-log-latency-buckets-" + Guid.NewGuid().ToString("N"));
        string databasePath = Path.Combine(root, "mail-log-inspector.sqlite");
        var store = new MailLogInspectorStore(databasePath);
        store.Initialize();
        DateTime accepted = new(2026, 7, 10, 8, 0, 0);
        store.SaveImport(
            "latency.csv",
            "latency-hash",
            null,
            new[]
            {
                Entry(1, accepted, "D", 60),
                Entry(2, accepted.AddMinutes(1), "D", 300),
                Entry(3, accepted.AddMinutes(2), "D", 900),
                Entry(4, accepted.AddMinutes(3), "D", 3600),
                Entry(5, accepted.AddMinutes(4), "D", 3601),
                Entry(6, accepted.AddMinutes(5), "D", 30),
                Entry(7, accepted.AddMinutes(6), "B", 120),
                Entry(8, accepted.AddMinutes(7), "Q", null),
            },
            errorCount: 0);

        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "UPDATE mail_items SET duration_seconds = NULL WHERE mail_item_id = (SELECT MIN(mail_item_id) FROM mail_items WHERE status = 1);";
            command.ExecuteNonQuery();
        }
        store.RebuildAnalysisData();

        using var verifyConnection = new SqliteConnection($"Data Source={databasePath}");
        verifyConnection.Open();
        using SqliteCommand verifyCommand = verifyConnection.CreateCommand();
        verifyCommand.CommandText = """
            SELECT total, duration_metrics_version, duration_count, duration_sum_seconds,
                   duration_missing_count, within_60_count, within_300_count,
                   within_900_count, within_3600_count
            FROM analysis_daily_status
            WHERE day_key = $dayKey AND status = 1;
            """;
        verifyCommand.Parameters.AddWithValue("$dayKey", accepted.Date.Ticks / TimeSpan.TicksPerDay);
        using SqliteDataReader reader = verifyCommand.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(6, reader.GetInt32(0));
        Assert.Equal(1, reader.GetInt32(1));
        Assert.Equal(5, reader.GetInt32(2));
        Assert.Equal(8431, reader.GetInt64(3));
        Assert.Equal(1, reader.GetInt32(4));
        Assert.Equal(1, reader.GetInt32(5));
        Assert.Equal(2, reader.GetInt32(6));
        Assert.Equal(3, reader.GetInt32(7));
        Assert.Equal(4, reader.GetInt32(8));
    }

    [Fact]
    public void ReadDeliveryLatencyTrend_ReadsPersistedDailyRowsWithoutMailItems()
    {
        string root = Path.Combine(Path.GetTempPath(), "mail-log-latency-read-" + Guid.NewGuid().ToString("N"));
        string databasePath = Path.Combine(root, "mail-log-inspector.sqlite");
        var store = new MailLogInspectorStore(databasePath);
        store.Initialize();
        DateTime day = new(2026, 7, 10);
        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO analysis_daily_status (
                    day_key, status, total, duration_metrics_version, duration_count,
                    duration_sum_seconds, duration_missing_count, within_60_count,
                    within_300_count, within_900_count, within_3600_count)
                VALUES ($dayKey, 1, 100, 1, 98, 5880, 2, 70, 90, 96, 98);
                """;
            command.Parameters.AddWithValue("$dayKey", day.Ticks / TimeSpan.TicksPerDay);
            command.ExecuteNonQuery();
        }

        MethodInfo? method = typeof(MailLogInspectorStore).GetMethod("ReadDeliveryLatencyTrend");

        Assert.NotNull(method);
        var rows = Assert.IsAssignableFrom<IEnumerable>(method.Invoke(store, new object[] { day, day })!);
        object row = Assert.Single(rows.Cast<object>());
        Assert.Equal(day, ReadProperty<DateTime>(row, "Date"));
        Assert.Equal(100, ReadProperty<int>(row, "DeliveredCount"));
        Assert.Equal(98, ReadProperty<int>(row, "DurationCount"));
        Assert.Equal(5880L, ReadProperty<long>(row, "DurationSumSeconds"));
        Assert.Equal(90, ReadProperty<int>(row, "Within300Count"));
    }

    [Fact]
    public void EnsureDeliveryLatencyAggregates_RebuildsOnceWhenVersionIsMissing()
    {
        string root = Path.Combine(Path.GetTempPath(), "mail-log-latency-backfill-" + Guid.NewGuid().ToString("N"));
        string databasePath = Path.Combine(root, "mail-log-inspector.sqlite");
        var store = new MailLogInspectorStore(databasePath);
        store.Initialize();
        DateTime accepted = new(2026, 7, 10, 8, 0, 0);
        store.SaveImport("latency.csv", "backfill-hash", null, new[] { Entry(1, accepted, "D", 120) }, 0);
        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "UPDATE analysis_daily_status SET duration_metrics_version = 0, duration_count = 0, duration_sum_seconds = 0;";
            command.ExecuteNonQuery();
        }
        MethodInfo? method = typeof(MailLogInspectorStore).GetMethod("EnsureDeliveryLatencyAggregates");

        Assert.NotNull(method);
        Assert.True((bool)method.Invoke(store, null)!);
        Assert.False((bool)method.Invoke(store, null)!);
    }

    private static T ReadProperty<T>(object value, string propertyName)
    {
        return (T)value.GetType().GetProperty(propertyName)!.GetValue(value)!;
    }
    private static SmtpLogEntry Entry(int row, DateTime accepted, string status, int? durationSeconds)
    {
        return new SmtpLogEntry(
            row,
            accepted,
            durationSeconds.HasValue ? accepted.AddSeconds(durationSeconds.Value) : null,
            "sender@example.com",
            "example.com",
            $"recipient-{row}@example.net",
            "example.net",
            status,
            status == "D" ? "250" : "550",
            status == "D" ? "delivered" : "status",
            string.Empty,
            null,
            string.Empty,
            $"tracking-{row}",
            string.Empty);
    }
}