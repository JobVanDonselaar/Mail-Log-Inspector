using System.Globalization;
using MailLogInspector.Core;
using Microsoft.Data.Sqlite;

namespace MailLogInspector.Storage;

public sealed partial class MailLogInspectorStore
{
    private const int BounceStatusCode = 3;
    private const int DeliveredStatusCode = 1;
    private const int UnderwayStatusCode = 2;

    /// <summary>
    /// Groepeert de bounces van één import per afzender-e-mailadres. Afzenders zonder bounces
    /// worden weggelaten omdat daarvoor geen melding nodig is.
    /// </summary>
    public IReadOnlyList<MailLogInspectorSenderBounceReport> ReadSenderBounceReports(long importId)
    {
        using SqliteConnection connection = OpenConnection();
        return ReadSenderBounceReports(connection, importId);
    }

    private static IReadOnlyList<MailLogInspectorSenderBounceReport> ReadSenderBounceReports(
        SqliteConnection connection,
        long importId)
    {
        Dictionary<string, (int Total, int Delivered, int Underway, int Bounce)> totals =
            ReadSenderTotals(connection, importId);

        Dictionary<string, List<MailLogInspectorBounceRow>> bouncesBySender =
            ReadBounceRows(connection, importId);

        List<MailLogInspectorSenderBounceReport> reports = [];
        foreach ((string sender, List<MailLogInspectorBounceRow> bounces) in bouncesBySender)
        {
            totals.TryGetValue(sender, out (int Total, int Delivered, int Underway, int Bounce) counts);
            reports.Add(new MailLogInspectorSenderBounceReport(
                sender,
                counts.Total,
                counts.Delivered,
                counts.Underway,
                counts.Bounce == 0 ? bounces.Count : counts.Bounce,
                bounces));
        }

        return reports
            .OrderByDescending(report => report.BounceCount)
            .ThenBy(report => report.SenderAddress, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, (int Total, int Delivered, int Underway, int Bounce)> ReadSenderTotals(
        SqliteConnection connection,
        long importId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT LOWER(sender.local_part || '@' || COALESCE(sender_domain.domain_name, '')),
                   COUNT(*),
                   SUM(CASE WHEN item.status = $delivered THEN 1 ELSE 0 END),
                   SUM(CASE WHEN item.status = $underway THEN 1 ELSE 0 END),
                   SUM(CASE WHEN item.status = $bounce THEN 1 ELSE 0 END)
            FROM mail_items AS item
            JOIN mail_addresses AS sender ON sender.address_id = item.sender_address_id
            LEFT JOIN mail_domains AS sender_domain ON sender_domain.domain_id = item.sender_domain_id
            WHERE item.last_import_id = $importId
            GROUP BY 1;
            """;
        command.Parameters.AddWithValue("$importId", importId);
        command.Parameters.AddWithValue("$delivered", DeliveredStatusCode);
        command.Parameters.AddWithValue("$underway", UnderwayStatusCode);
        command.Parameters.AddWithValue("$bounce", BounceStatusCode);

        Dictionary<string, (int, int, int, int)> totals = new(StringComparer.OrdinalIgnoreCase);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string sender = reader.GetString(0);
            totals[sender] = (
                (int)reader.GetInt64(1),
                reader.IsDBNull(2) ? 0 : (int)reader.GetInt64(2),
                reader.IsDBNull(3) ? 0 : (int)reader.GetInt64(3),
                reader.IsDBNull(4) ? 0 : (int)reader.GetInt64(4));
        }

        return totals;
    }

    private static Dictionary<string, List<MailLogInspectorBounceRow>> ReadBounceRows(
        SqliteConnection connection,
        long importId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT LOWER(sender.local_part || '@' || COALESCE(sender_domain.domain_name, '')),
                   item.accepted_at,
                   recipient.local_part || '@' || COALESCE(recipient_domain.domain_name, ''),
                   item.reason_code,
                   item.response_code,
                   item.last_seen_at
            FROM mail_items AS item
            JOIN mail_addresses AS sender ON sender.address_id = item.sender_address_id
            LEFT JOIN mail_domains AS sender_domain ON sender_domain.domain_id = item.sender_domain_id
            JOIN mail_addresses AS recipient ON recipient.address_id = item.recipient_address_id
            LEFT JOIN mail_domains AS recipient_domain ON recipient_domain.domain_id = item.recipient_domain_id
            WHERE item.last_import_id = $importId
              AND item.status = $bounce
            ORDER BY 1, item.accepted_at DESC;
            """;
        command.Parameters.AddWithValue("$importId", importId);
        command.Parameters.AddWithValue("$bounce", BounceStatusCode);

        Dictionary<string, List<MailLogInspectorBounceRow>> grouped = new(StringComparer.OrdinalIgnoreCase);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string sender = reader.GetString(0);
            var reasonCode = (MailLogInspectorReasonCode)(reader.IsDBNull(3) ? 0 : reader.GetInt64(3));

            var row = new MailLogInspectorBounceRow(
                reader.IsDBNull(1) ? null : FromUnixSeconds(reader.GetInt64(1)),
                reader.GetString(2),
                reasonCode,
                reader.IsDBNull(4) ? null : (int)reader.GetInt64(4),
                MailLogInspectorAttemptMeaning.DescribeBounceStatus(reasonCode));

            if (!grouped.TryGetValue(sender, out List<MailLogInspectorBounceRow>? rows))
            {
                rows = [];
                grouped[sender] = rows;
            }

            rows.Add(row);
        }

        return grouped;
    }

    /// <summary>Het meest recente import-id, of null wanneer er nog niets is geïmporteerd.</summary>
    public long? ReadLatestImportId()
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(import_id) FROM imports;";
        object? result = command.ExecuteScalar();
        return result is null || result is DBNull ? null : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static DateTime FromUnixSeconds(long seconds)
    {
        return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime.ToLocalTime();
    }
}
