using MailLogInspector.Core;
using Microsoft.Data.Sqlite;

namespace MailLogInspector.Storage;

public sealed partial class MailLogInspectorStore
{
    public IReadOnlyList<MailLogInspectorDeliveryLatencyDay> ReadDeliveryLatencyTrend(
        DateTime fromInclusive,
        DateTime throughInclusive)
    {
        using SqliteConnection connection = OpenConnection();
        MailLogInspectorSchema.Ensure(connection);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT day_key, total, duration_count, duration_sum_seconds,
                   duration_missing_count, within_60_count, within_300_count,
                   within_900_count, within_3600_count
            FROM analysis_daily_status
            WHERE status = 1
              AND duration_metrics_version = 1
              AND day_key >= $fromDay
              AND day_key <= $throughDay
            ORDER BY day_key DESC
            LIMIT 30;
            """;
        command.Parameters.AddWithValue("$fromDay", fromInclusive.Date.Ticks / TimeSpan.TicksPerDay);
        command.Parameters.AddWithValue("$throughDay", throughInclusive.Date.Ticks / TimeSpan.TicksPerDay);
        using SqliteDataReader reader = command.ExecuteReader();
        var rows = new List<MailLogInspectorDeliveryLatencyDay>();
        while (reader.Read())
        {
            rows.Add(new MailLogInspectorDeliveryLatencyDay(
                new DateTime(reader.GetInt64(0) * TimeSpan.TicksPerDay),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt64(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetInt32(8)));
        }
        rows.Reverse();
        return rows;
    }

    public MailLogInspectorDailyStatusTotals ReadDailyStatusTotals(DateTime day)
    {
        using SqliteConnection connection = OpenConnection();
        MailLogInspectorSchema.Ensure(connection);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT status, SUM(total)
            FROM analysis_daily_status
            WHERE day_key = $day
            GROUP BY status;
            """;
        command.Parameters.AddWithValue("$day", day.Date.Ticks / TimeSpan.TicksPerDay);
        using SqliteDataReader reader = command.ExecuteReader();
        int accepted = 0;
        int delivered = 0;
        int bounce = 0;
        bool hasData = false;
        while (reader.Read())
        {
            hasData = true;
            int status = (int)reader.GetInt64(0);
            int total = (int)reader.GetInt64(1);
            accepted += total;
            if (status == 1)
            {
                delivered += total;
            }
            else if (status == 3)
            {
                bounce += total;
            }
        }

        return new MailLogInspectorDailyStatusTotals(day.Date, accepted, delivered, bounce, hasData);
    }

    public bool EnsureDeliveryLatencyAggregates()
    {
        using SqliteConnection connection = OpenConnection();
        MailLogInspectorSchema.Ensure(connection);
        using SqliteCommand check = connection.CreateCommand();
        check.CommandText = """
            SELECT EXISTS(
                SELECT 1
                FROM analysis_daily_status
                WHERE status = 1
                  AND total > 0
                  AND duration_metrics_version < 1
                LIMIT 1
            );
            """;
        if (Convert.ToInt32(check.ExecuteScalar()) == 0)
        {
            return false;
        }

        RebuildDailyStatusTable(connection);
        return true;
    }

    private static void RebuildDailyStatusTable(SqliteConnection connection)
    {
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM analysis_daily_status;

            INSERT INTO analysis_daily_status (
                day_key, status, total, duration_metrics_version, duration_count,
                duration_sum_seconds, duration_missing_count, within_60_count,
                within_300_count, within_900_count, within_3600_count)
            SELECT accepted_at / 864000000000,
                   status,
                   COUNT(*),
                   1,
                   COUNT(duration_seconds),
                   COALESCE(SUM(duration_seconds), 0),
                   SUM(CASE WHEN duration_seconds IS NULL THEN 1 ELSE 0 END),
                   SUM(CASE WHEN duration_seconds IS NOT NULL AND duration_seconds <= 60 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN duration_seconds IS NOT NULL AND duration_seconds <= 300 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN duration_seconds IS NOT NULL AND duration_seconds <= 900 THEN 1 ELSE 0 END),
                   SUM(CASE WHEN duration_seconds IS NOT NULL AND duration_seconds <= 3600 THEN 1 ELSE 0 END)
            FROM mail_items
            WHERE accepted_at IS NOT NULL
            GROUP BY accepted_at / 864000000000, status;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
