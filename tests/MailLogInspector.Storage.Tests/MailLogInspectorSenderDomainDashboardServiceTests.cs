using MailLogInspector.Core;
using MailLogInspector.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class MailLogInspectorSenderDomainDashboardServiceTests
{
    [Fact]
    public void ReadSenderDomainDashboard_ReadsFullPeriodTotalsRecentTrendAndNormalizedCausesFromAggregates()
    {
        using var harness = AggregateHarness.Create();
        harness.AddDomain(7, "example.com");
        harness.AddDay(new DateTime(2026, 7, 1), 7, total: 10, delivered: 6, underway: 1, bounce: 3,
            durationCount: 5, durationSum: 500, missing: 1, within60: 2, within300: 4, within900: 5, within3600: 5);
        harness.AddDay(new DateTime(2026, 7, 3), 7, total: 20, delivered: 15, underway: 3, bounce: 2,
            durationCount: 13, durationSum: 2_600, missing: 2, within60: 5, within300: 13, within900: 13, within3600: 13);
        harness.AddDay(new DateTime(2026, 7, 8), 7, total: 30, delivered: 25, underway: 2, bounce: 3,
            durationCount: 20, durationSum: 6_500, missing: 5, within60: 4, within300: 12, within900: 18, within3600: 19);
        harness.AddReason(new DateTime(2026, 7, 1), 7, MailLogInspectorReasonCode.MailboxFull, 2);
        harness.AddReason(new DateTime(2026, 7, 3), 7, MailLogInspectorReasonCode.MailboxFull, 3);
        harness.AddReason(new DateTime(2026, 7, 8), 7, MailLogInspectorReasonCode.InvalidRecipient, 5);
        harness.AddReason(new DateTime(2026, 7, 8), 7, MailLogInspectorReasonCode.DnsProblem, 1);

        var service = new MailLogInspectorSenderDomainDashboardService(harness.Store);

        MailLogInspectorSenderDomainDashboard dashboard = service.ReadSenderDomainDashboard(
            Criteria(" EXAMPLE.COM ", status: "bounce"), trendDayLimit: 2, causeLimit: 2);

        Assert.Equal("example.com", dashboard.Domain);
        Assert.Equal(new DateTime(2026, 7, 1), dashboard.FromDate);
        Assert.Equal(TimeSpan.Zero, dashboard.FromDate.TimeOfDay);
        Assert.Equal(new DateTime(2026, 7, 31), dashboard.ThroughDate);
        Assert.Equal(TimeSpan.Zero, dashboard.ThroughDate.TimeOfDay);
        Assert.Equal(60, dashboard.TotalCount);
        Assert.Equal(46, dashboard.DeliveredCount);
        Assert.Equal(6, dashboard.UnderwayCount);
        Assert.Equal(8, dashboard.BounceCount);
        Assert.Equal(38, dashboard.DurationCount);
        Assert.Equal(8, dashboard.DurationMissingCount);
        Assert.Equal(9_600d / 38d, dashboard.AverageDurationSeconds);
        Assert.Equal(MailLogInspectorDurationBucket.WithinOneHour, dashboard.P95DurationBucket);
        Assert.Equal(38, dashboard.DurationDistribution.DurationCount);
        Assert.Equal(8, dashboard.DurationDistribution.MissingCount);
        Assert.Equal(11, dashboard.DurationDistribution.WithinOneMinute);
        Assert.Equal(18, dashboard.DurationDistribution.OneToFiveMinutes);
        Assert.Equal(7, dashboard.DurationDistribution.FiveToFifteenMinutes);
        Assert.Equal(1, dashboard.DurationDistribution.FifteenToSixtyMinutes);
        Assert.Equal(1, dashboard.DurationDistribution.OverOneHour);

        Assert.Collection(dashboard.Trend,
            day =>
            {
                Assert.Equal(new DateTime(2026, 7, 3), day.Date);
                Assert.Equal(20, day.TotalCount);
                Assert.Equal(15, day.DeliveredCount);
                Assert.Equal(13, day.DurationCount);
                Assert.Equal(2, day.MissingDurationCount);
                Assert.Equal(200d, day.AverageDurationSeconds);
                Assert.Equal(MailLogInspectorDurationBucket.WithinFiveMinutes, day.P95DurationBucket);
            },
            day =>
            {
                Assert.Equal(new DateTime(2026, 7, 8), day.Date);
                Assert.Equal(30, day.TotalCount);
                Assert.Equal(25, day.DeliveredCount);
                Assert.Equal(20, day.DurationCount);
                Assert.Equal(5, day.MissingDurationCount);
                Assert.Equal(MailLogInspectorDurationBucket.WithinOneHour, day.P95DurationBucket);
            });

        Assert.Collection(dashboard.TopCauses,
            cause =>
            {
                Assert.Equal(MailLogInspectorReasonCode.MailboxFull, cause.ReasonCode);
                Assert.Equal("Mailbox vol", cause.Description);
                Assert.Equal(5, cause.Count);
            },
            cause =>
            {
                Assert.Equal(MailLogInspectorReasonCode.InvalidRecipient, cause.ReasonCode);
                Assert.Equal("Adres ongeldig", cause.Description);
                Assert.Equal(5, cause.Count);
            });
    }

    [Fact]
    public void ReadSenderDomainDashboard_ReturnsEmptyDashboardForUnknownDomain()
    {
        using var harness = AggregateHarness.Create();
        var service = new MailLogInspectorSenderDomainDashboardService(harness.Store);

        MailLogInspectorSenderDomainDashboard dashboard = service.ReadSenderDomainDashboard(Criteria("missing.example"));

        Assert.Equal("missing.example", dashboard.Domain);
        Assert.Equal(new DateTime(2026, 7, 1), dashboard.FromDate);
        Assert.Equal(new DateTime(2026, 7, 31), dashboard.ThroughDate);
        Assert.Equal(0, dashboard.TotalCount);
        Assert.Equal(0, dashboard.DurationCount);
        Assert.Equal(0, dashboard.DurationMissingCount);
        Assert.Null(dashboard.AverageDurationSeconds);
        Assert.Null(dashboard.P95DurationBucket);
        Assert.Equal(MailLogInspectorDurationDistribution.Empty, dashboard.DurationDistribution);
        Assert.Empty(dashboard.Trend);
        Assert.Empty(dashboard.TopCauses);
    }

    [Fact]
    public void ReadSenderDomainDashboard_ClampsInvalidCumulativeDurationCounts()
    {
        using var harness = AggregateHarness.Create();
        harness.AddDomain(7, "example.com");
        harness.AddDay(new DateTime(2026, 7, 1), 7, total: 10, delivered: 10, underway: 0, bounce: 0,
            durationCount: 10, durationSum: 600, missing: 0,
            within60: 8, within300: 5, within900: -2, within3600: 20);

        var service = new MailLogInspectorSenderDomainDashboardService(harness.Store);

        MailLogInspectorDurationDistribution distribution = service
            .ReadSenderDomainDashboard(Criteria("example.com"))
            .DurationDistribution;

        Assert.Equal(10, distribution.DurationCount);
        Assert.Equal(8, distribution.WithinOneMinute);
        Assert.Equal(0, distribution.OneToFiveMinutes);
        Assert.Equal(0, distribution.FiveToFifteenMinutes);
        Assert.Equal(2, distribution.FifteenToSixtyMinutes);
        Assert.Equal(0, distribution.OverOneHour);
    }
    [Theory]
    [InlineData("sender@example.com", null, null, "example.com")]
    [InlineData(null, "recipient@example.net", null, "example.com")]
    [InlineData(null, null, "example.net", "example.com")]
    [InlineData(null, null, null, null)]
    public void ReadSenderDomainDashboard_RejectsCriteriaOtherThanSenderDomainOnly(
        string? sender,
        string? recipient,
        string? recipientDomain,
        string? senderDomain)
    {
        using var harness = AggregateHarness.Create();
        var service = new MailLogInspectorSenderDomainDashboardService(harness.Store);
        var criteria = new MailLogInspectorSearchCriteria(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 31, 23, 59, 59),
            sender,
            recipient,
            senderDomain,
            recipientDomain,
            null);

        Assert.Throws<ArgumentException>(() => service.ReadSenderDomainDashboard(criteria));
    }

    [Theory]
    [InlineData(0, 4, "trendDayLimit")]
    [InlineData(30, 0, "causeLimit")]
    public void ReadSenderDomainDashboard_RejectsNonPositiveLimits(int trendDayLimit, int causeLimit, string parameterName)
    {
        using var harness = AggregateHarness.Create();
        var service = new MailLogInspectorSenderDomainDashboardService(harness.Store);

        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.ReadSenderDomainDashboard(Criteria("example.com"), trendDayLimit, causeLimit));

        Assert.Equal(parameterName, error.ParamName);
    }

    [Fact]
    public void ReadSenderDomainDashboard_HonorsPreCancelledToken()
    {
        using var harness = AggregateHarness.Create();
        var service = new MailLogInspectorSenderDomainDashboardService(harness.Store);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            service.ReadSenderDomainDashboard(Criteria("example.com"), cancellationToken: cancellation.Token));
    }

    private static MailLogInspectorSearchCriteria Criteria(string senderDomain, string? status = null) =>
        new(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 31, 23, 59, 59),
            null,
            null,
            senderDomain,
            null,
            status);

    private sealed class AggregateHarness : IDisposable
    {
        private readonly string _directory;
        private readonly SqliteConnection _connection;

        private AggregateHarness(string directory, SqliteConnection connection, MailLogInspectorStore store)
        {
            _directory = directory;
            _connection = connection;
            Store = store;
        }

        public MailLogInspectorStore Store { get; }

        public static AggregateHarness Create()
        {
            string directory = Path.Combine(Path.GetTempPath(), "mail-log-dashboard-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string databasePath = Path.Combine(directory, "aggregates.sqlite");
            var connection = new SqliteConnection($"Data Source={databasePath}");
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE mail_domains (
                    domain_id INTEGER PRIMARY KEY,
                    domain_name TEXT NOT NULL
                );
                CREATE TABLE analysis_daily_sender_domain (
                    day_key INTEGER NOT NULL,
                    domain_id INTEGER NOT NULL,
                    total INTEGER NOT NULL,
                    delivered INTEGER NOT NULL,
                    underway INTEGER NOT NULL,
                    bounce INTEGER NOT NULL,
                    duration_metrics_version INTEGER NOT NULL,
                    duration_count INTEGER NOT NULL,
                    duration_sum_seconds INTEGER NOT NULL,
                    duration_missing_count INTEGER NOT NULL,
                    within_60_count INTEGER NOT NULL,
                    within_300_count INTEGER NOT NULL,
                    within_900_count INTEGER NOT NULL,
                    within_3600_count INTEGER NOT NULL,
                    PRIMARY KEY(day_key, domain_id)
                ) WITHOUT ROWID;
                CREATE TABLE analysis_daily_sender_reason (
                    day_key INTEGER NOT NULL,
                    domain_id INTEGER NOT NULL,
                    reason_code INTEGER NOT NULL,
                    total INTEGER NOT NULL,
                    PRIMARY KEY(day_key, domain_id, reason_code)
                ) WITHOUT ROWID;
                """;
            command.ExecuteNonQuery();
            return new AggregateHarness(directory, connection, new MailLogInspectorStore(databasePath));
        }

        public void AddDomain(long domainId, string domainName)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = "INSERT INTO mail_domains (domain_id, domain_name) VALUES ($id, $name);";
            command.Parameters.AddWithValue("$id", domainId);
            command.Parameters.AddWithValue("$name", domainName);
            command.ExecuteNonQuery();
        }

        public void AddDay(
            DateTime date,
            long domainId,
            int total,
            int delivered,
            int underway,
            int bounce,
            int durationCount,
            long durationSum,
            int missing,
            int within60,
            int within300,
            int within900,
            int within3600)
        {
            Assert.Equal(delivered, durationCount + missing);
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO analysis_daily_sender_domain (
                    day_key, domain_id, total, delivered, underway, bounce,
                    duration_metrics_version, duration_count, duration_sum_seconds,
                    duration_missing_count, within_60_count, within_300_count,
                    within_900_count, within_3600_count)
                VALUES (
                    $day, $domain, $total, $delivered, $underway, $bounce,
                    1, $durationCount, $durationSum, $missing,
                    $within60, $within300, $within900, $within3600);
                """;
            command.Parameters.AddWithValue("$day", date.Date.Ticks / TimeSpan.TicksPerDay);
            command.Parameters.AddWithValue("$domain", domainId);
            command.Parameters.AddWithValue("$total", total);
            command.Parameters.AddWithValue("$delivered", delivered);
            command.Parameters.AddWithValue("$underway", underway);
            command.Parameters.AddWithValue("$bounce", bounce);
            command.Parameters.AddWithValue("$durationCount", durationCount);
            command.Parameters.AddWithValue("$durationSum", durationSum);
            command.Parameters.AddWithValue("$missing", missing);
            command.Parameters.AddWithValue("$within60", within60);
            command.Parameters.AddWithValue("$within300", within300);
            command.Parameters.AddWithValue("$within900", within900);
            command.Parameters.AddWithValue("$within3600", within3600);
            command.ExecuteNonQuery();
        }

        public void AddReason(DateTime date, long domainId, MailLogInspectorReasonCode reason, int total)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO analysis_daily_sender_reason (day_key, domain_id, reason_code, total)
                VALUES ($day, $domain, $reason, $total);
                """;
            command.Parameters.AddWithValue("$day", date.Date.Ticks / TimeSpan.TicksPerDay);
            command.Parameters.AddWithValue("$domain", domainId);
            command.Parameters.AddWithValue("$reason", (int)reason);
            command.Parameters.AddWithValue("$total", total);
            command.ExecuteNonQuery();
        }

        public void Dispose()
        {
            _connection.Dispose();
            SqliteConnection.ClearAllPools();
            Directory.Delete(_directory, recursive: true);
        }
    }
}
