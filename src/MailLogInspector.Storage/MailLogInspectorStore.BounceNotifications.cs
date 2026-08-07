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
        return ReadSenderBounceReports(BounceReportScope.ForImport(importId));
    }

    /// <summary>
    /// Groepeert de bounces binnen een datumbereik per afzender-e-mailadres. Hiermee kan een
    /// overgeslagen dag of week alsnog gemeld worden, los van welke import de regels bracht.
    /// </summary>
    public IReadOnlyList<MailLogInspectorSenderBounceReport> ReadSenderBounceReports(
        DateTime fromInclusive,
        DateTime throughInclusive)
    {
        return ReadSenderBounceReports(BounceReportScope.ForPeriod(fromInclusive, throughInclusive));
    }

    private IReadOnlyList<MailLogInspectorSenderBounceReport> ReadSenderBounceReports(BounceReportScope scope)
    {
        using SqliteConnection connection = OpenConnection();

        Dictionary<string, (int Total, int Delivered, int Underway, int Bounce)> totals =
            ReadSenderTotals(connection, scope);

        Dictionary<string, List<MailLogInspectorBounceRow>> bouncesBySender =
            ReadBounceRows(connection, scope);

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
        BounceReportScope scope)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT LOWER(sender.local_part || '@' || COALESCE(sender_domain.domain_name, '')),
                   COUNT(*),
                   SUM(CASE WHEN item.status = $delivered THEN 1 ELSE 0 END),
                   SUM(CASE WHEN item.status = $underway THEN 1 ELSE 0 END),
                   SUM(CASE WHEN item.status = $bounce THEN 1 ELSE 0 END)
            FROM mail_items AS item
            JOIN mail_addresses AS sender ON sender.address_id = item.sender_address_id
            LEFT JOIN mail_domains AS sender_domain ON sender_domain.domain_id = item.sender_domain_id
            WHERE {scope.WhereClause}
            GROUP BY 1;
            """;
        scope.AddParameters(command);
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
        BounceReportScope scope)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
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
            WHERE {scope.WhereClause}
              AND item.status = $bounce
            ORDER BY 1, item.accepted_at DESC;
            """;
        scope.AddParameters(command);
        command.Parameters.AddWithValue("$bounce", BounceStatusCode);

        Dictionary<string, List<MailLogInspectorBounceRow>> grouped = new(StringComparer.OrdinalIgnoreCase);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string sender = reader.GetString(0);
            var reasonCode = (MailLogInspectorReasonCode)(reader.IsDBNull(3) ? 0 : reader.GetInt64(3));

            var row = new MailLogInspectorBounceRow(
                reader.IsDBNull(1) ? null : FromStoredTicks(reader.GetInt64(1)),
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

    /// <summary>
    /// Bepaalt over welke regels een bouncerapport gaat: één import of een datumbereik.
    /// Beide varianten delen dezelfde queries zodat de cijfers gelijk blijven.
    /// </summary>
    private sealed class BounceReportScope
    {
        private readonly long? _importId;
        private readonly DateTime? _fromInclusive;
        private readonly DateTime? _throughInclusive;

        private BounceReportScope(long? importId, DateTime? fromInclusive, DateTime? throughInclusive)
        {
            _importId = importId;
            _fromInclusive = fromInclusive;
            _throughInclusive = throughInclusive;
        }

        public static BounceReportScope ForImport(long importId) => new(importId, null, null);

        public static BounceReportScope ForPeriod(DateTime fromInclusive, DateTime throughInclusive)
        {
            DateTime start = fromInclusive.Date;
            DateTime end = throughInclusive.Date.AddDays(1).AddTicks(-1);

            if (end < start)
            {
                (start, end) = (end.Date, start.Date.AddDays(1).AddTicks(-1));
            }

            return new BounceReportScope(null, start, end);
        }

        public string WhereClause => _importId.HasValue
            ? "item.last_import_id = $importId"
            : "item.accepted_at >= $fromInclusive AND item.accepted_at <= $throughInclusive";

        public void AddParameters(SqliteCommand command)
        {
            if (_importId.HasValue)
            {
                command.Parameters.AddWithValue("$importId", _importId.Value);
                return;
            }

            command.Parameters.AddWithValue("$fromInclusive", ToStoredTicks(_fromInclusive!.Value));
            command.Parameters.AddWithValue("$throughInclusive", ToStoredTicks(_throughInclusive!.Value));
        }
    }
}
