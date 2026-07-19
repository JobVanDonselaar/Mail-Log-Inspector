using MailLogInspector.Core;
using Microsoft.Data.Sqlite;

namespace MailLogInspector.Storage;

public sealed class MailLogInspectorSenderDomainDashboardService
{
    private readonly MailLogInspectorStore _store;

    public MailLogInspectorSenderDomainDashboardService(MailLogInspectorStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public MailLogInspectorSenderDomainDashboard ReadSenderDomainDashboard(
        MailLogInspectorSearchCriteria criteria,
        int trendDayLimit = 30,
        int causeLimit = 4,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (trendDayLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(trendDayLimit));
        }
        if (causeLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(causeLimit));
        }

        string domain = ValidateAndNormalizeCriteria(criteria);
        cancellationToken.ThrowIfCancellationRequested();

        using SqliteConnection connection = _store.OpenConnection();
        (long DomainId, string Domain)? resolvedDomain = ResolveDomain(connection, domain, cancellationToken);
        if (!resolvedDomain.HasValue)
        {
            return EmptyDashboard(domain, criteria);
        }

        cancellationToken.ThrowIfCancellationRequested();
        AggregateRow totals = ReadTotals(connection, resolvedDomain.Value.DomainId, criteria, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<MailLogInspectorSenderDomainTrendDay> trend = ReadTrend(
            connection,
            resolvedDomain.Value.DomainId,
            criteria,
            trendDayLimit,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<MailLogInspectorSenderDomainCause> causes = ReadCauses(
            connection,
            resolvedDomain.Value.DomainId,
            criteria,
            causeLimit,
            cancellationToken);

        return new MailLogInspectorSenderDomainDashboard(
            resolvedDomain.Value.Domain,
            criteria.FromInclusive,
            criteria.ThroughInclusive,
            totals.Total,
            totals.Delivered,
            totals.Underway,
            totals.Bounce,
            totals.DurationCount,
            totals.MissingDurationCount,
            CalculateAverage(totals),
            CalculateP95Bucket(totals),
            trend,
            causes)
        {
            DurationDistribution = CalculateDurationDistribution(totals)
        };
    }

    private static string ValidateAndNormalizeCriteria(MailLogInspectorSearchCriteria criteria)
    {
        if (criteria.FromInclusive > criteria.ThroughInclusive)
        {
            throw new ArgumentException("The start of the period must not be after its end.", nameof(criteria));
        }
        if (string.IsNullOrWhiteSpace(criteria.SenderDomain) ||
            !string.IsNullOrWhiteSpace(criteria.Sender) ||
            !string.IsNullOrWhiteSpace(criteria.Recipient) ||
            !string.IsNullOrWhiteSpace(criteria.RecipientDomain))
        {
            throw new ArgumentException("Sender-domain dashboard criteria must contain only a sender domain.", nameof(criteria));
        }

        return criteria.SenderDomain.Trim().ToLowerInvariant();
    }

    private static (long DomainId, string Domain)? ResolveDomain(
        SqliteConnection connection,
        string domain,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT domain_id, domain_name
            FROM mail_domains
            WHERE domain_name = $domain COLLATE NOCASE
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$domain", domain);
        command.CommandTimeout = 30;
        using CancellationTokenRegistration registration = cancellationToken.Register(command.Cancel);
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetInt64(0), reader.GetString(1)) : null;
    }

    private static AggregateRow ReadTotals(
        SqliteConnection connection,
        long domainId,
        MailLogInspectorSearchCriteria criteria,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(SUM(total), 0),
                   COALESCE(SUM(delivered), 0),
                   COALESCE(SUM(underway), 0),
                   COALESCE(SUM(bounce), 0),
                   COALESCE(SUM(duration_count), 0),
                   COALESCE(SUM(duration_sum_seconds), 0),
                   COALESCE(SUM(duration_missing_count), 0),
                   COALESCE(SUM(within_60_count), 0),
                   COALESCE(SUM(within_300_count), 0),
                   COALESCE(SUM(within_900_count), 0),
                   COALESCE(SUM(within_3600_count), 0)
            FROM analysis_daily_sender_domain
            WHERE domain_id = $domainId
              AND day_key >= $fromDay
              AND day_key <= $throughDay;
            """;
        AddParameters(command, domainId, criteria);
        command.CommandTimeout = 30;
        using CancellationTokenRegistration registration = cancellationToken.Register(command.Cancel);
        using SqliteDataReader reader = command.ExecuteReader();
        reader.Read();
        return ReadAggregateRow(reader, 0);
    }

    private static IReadOnlyList<MailLogInspectorSenderDomainTrendDay> ReadTrend(
        SqliteConnection connection,
        long domainId,
        MailLogInspectorSearchCriteria criteria,
        int limit,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT day_key, total, delivered, underway, bounce,
                   duration_count, duration_sum_seconds, duration_missing_count,
                   within_60_count, within_300_count, within_900_count, within_3600_count
            FROM (
                SELECT day_key, total, delivered, underway, bounce,
                       duration_count, duration_sum_seconds, duration_missing_count,
                       within_60_count, within_300_count, within_900_count, within_3600_count
                FROM analysis_daily_sender_domain
                WHERE domain_id = $domainId
                  AND day_key >= $fromDay
                  AND day_key <= $throughDay
                ORDER BY day_key DESC
                LIMIT $limit
            )
            ORDER BY day_key ASC;
            """;
        AddParameters(command, domainId, criteria);
        command.Parameters.AddWithValue("$limit", limit);
        command.CommandTimeout = 30;
        using CancellationTokenRegistration registration = cancellationToken.Register(command.Cancel);
        using SqliteDataReader reader = command.ExecuteReader();
        List<MailLogInspectorSenderDomainTrendDay> rows = new();
        while (reader.Read())
        {
            AggregateRow aggregate = ReadAggregateRow(reader, 1);
            rows.Add(new MailLogInspectorSenderDomainTrendDay(
                new DateTime(checked(reader.GetInt64(0) * TimeSpan.TicksPerDay)),
                aggregate.Total,
                aggregate.Delivered,
                aggregate.Underway,
                aggregate.Bounce,
                aggregate.DurationCount,
                aggregate.MissingDurationCount,
                CalculateAverage(aggregate),
                CalculateP95Bucket(aggregate)));
        }
        return rows;
    }

    private static IReadOnlyList<MailLogInspectorSenderDomainCause> ReadCauses(
        SqliteConnection connection,
        long domainId,
        MailLogInspectorSearchCriteria criteria,
        int limit,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT reason_code, SUM(total)
            FROM analysis_daily_sender_reason
            WHERE domain_id = $domainId
              AND day_key >= $fromDay
              AND day_key <= $throughDay
            GROUP BY reason_code;
            """;
        AddParameters(command, domainId, criteria);
        command.CommandTimeout = 30;
        using CancellationTokenRegistration registration = cancellationToken.Register(command.Cancel);
        using SqliteDataReader reader = command.ExecuteReader();
        Dictionary<MailLogInspectorReasonCode, int> totals = new();
        while (reader.Read())
        {
            int rawReason = reader.GetInt32(0);
            MailLogInspectorReasonCode reason = Enum.IsDefined(typeof(MailLogInspectorReasonCode), rawReason)
                ? (MailLogInspectorReasonCode)rawReason
                : MailLogInspectorReasonCode.Other;
            int count = checked((int)reader.GetInt64(1));
            totals[reason] = checked(totals.GetValueOrDefault(reason) + count);
        }

        return totals
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .Take(limit)
            .Select(pair => new MailLogInspectorSenderDomainCause(
                pair.Key,
                MailLogInspectorAttemptMeaning.DescribeBounceStatus(pair.Key),
                pair.Value))
            .ToList();
    }

    private static void AddParameters(
        SqliteCommand command,
        long domainId,
        MailLogInspectorSearchCriteria criteria)
    {
        command.Parameters.AddWithValue("$domainId", domainId);
        command.Parameters.AddWithValue("$fromDay", criteria.FromInclusive.Ticks / TimeSpan.TicksPerDay);
        command.Parameters.AddWithValue("$throughDay", criteria.ThroughInclusive.Ticks / TimeSpan.TicksPerDay);
    }

    private static AggregateRow ReadAggregateRow(SqliteDataReader reader, int offset) =>
        new(
            checked((int)reader.GetInt64(offset)),
            checked((int)reader.GetInt64(offset + 1)),
            checked((int)reader.GetInt64(offset + 2)),
            checked((int)reader.GetInt64(offset + 3)),
            checked((int)reader.GetInt64(offset + 4)),
            reader.GetInt64(offset + 5),
            checked((int)reader.GetInt64(offset + 6)),
            checked((int)reader.GetInt64(offset + 7)),
            checked((int)reader.GetInt64(offset + 8)),
            checked((int)reader.GetInt64(offset + 9)),
            checked((int)reader.GetInt64(offset + 10)));

    private static double? CalculateAverage(AggregateRow row) =>
        row.DurationCount <= 0 ? null : row.DurationSumSeconds / (double)row.DurationCount;

    private static MailLogInspectorDurationBucket? CalculateP95Bucket(AggregateRow row)
    {
        if (row.DurationCount <= 0)
        {
            return null;
        }

        int target = checked((int)Math.Ceiling(row.DurationCount * 0.95));
        if (row.Within60 >= target)
        {
            return MailLogInspectorDurationBucket.WithinOneMinute;
        }
        if (row.Within300 >= target)
        {
            return MailLogInspectorDurationBucket.WithinFiveMinutes;
        }
        if (row.Within900 >= target)
        {
            return MailLogInspectorDurationBucket.WithinFifteenMinutes;
        }
        return row.Within3600 >= target
            ? MailLogInspectorDurationBucket.WithinOneHour
            : MailLogInspectorDurationBucket.OverOneHour;
    }

    private static MailLogInspectorDurationDistribution CalculateDurationDistribution(AggregateRow row)
    {
        int durationCount = Math.Max(0, row.DurationCount);
        int within60 = Math.Clamp(row.Within60, 0, durationCount);
        int within300 = Math.Clamp(row.Within300, within60, durationCount);
        int within900 = Math.Clamp(row.Within900, within300, durationCount);
        int within3600 = Math.Clamp(row.Within3600, within900, durationCount);

        return new MailLogInspectorDurationDistribution(
            durationCount,
            Math.Max(0, row.MissingDurationCount),
            within60,
            within300 - within60,
            within900 - within300,
            within3600 - within900,
            durationCount - within3600);
    }
    private static MailLogInspectorSenderDomainDashboard EmptyDashboard(
        string domain,
        MailLogInspectorSearchCriteria criteria) =>
        new(
            domain,
            criteria.FromInclusive,
            criteria.ThroughInclusive,
            0,
            0,
            0,
            0,
            0,
            0,
            null,
            null,
            Array.Empty<MailLogInspectorSenderDomainTrendDay>(),
            Array.Empty<MailLogInspectorSenderDomainCause>());

    private sealed record AggregateRow(
        int Total,
        int Delivered,
        int Underway,
        int Bounce,
        int DurationCount,
        long DurationSumSeconds,
        int MissingDurationCount,
        int Within60,
        int Within300,
        int Within900,
        int Within3600);
}
