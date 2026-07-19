using MailLogInspector.App;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class SmtpPortalReportSelectionTests
{
    [Fact]
    public void SelectRequiredWithEmptyHistoryReturnsOnlyNewestReport()
    {
        IReadOnlyList<SmtpPortalReport> reports = SmtpPortalReportMatcher.SelectRequired(
            CreateRows(16, 17, 18),
            latestReportDay: null,
            yesterday: new DateTime(2026, 7, 18),
            latestOnly: true);

        SmtpPortalReport report = Assert.Single(reports);
        Assert.Equal(new DateTime(2026, 7, 18), report.PeriodStart);
    }

    [Fact]
    public void SelectRequiredReturnsMissingDaysOldestFirst()
    {
        IReadOnlyList<SmtpPortalReport> reports = SmtpPortalReportMatcher.SelectRequired(
            CreateRows(18, 17, 16),
            latestReportDay: new DateTime(2026, 7, 16),
            yesterday: new DateTime(2026, 7, 18),
            latestOnly: false);

        Assert.Equal(
            [new DateTime(2026, 7, 17), new DateTime(2026, 7, 18)],
            reports.Select(report => report.PeriodStart).ToArray());
    }

    private static IReadOnlyList<SmtpPortalReportRow> CreateRows(params int[] days)
    {
        return days.Select(day => new SmtpPortalReportRow(
            $"NextGen_2026-07-{day:00}(00)_2026-07-{day + 1:00}(00) (delivered + bounced + queue) (raw_event_stream)",
            "Ready",
            "row-" + day)).ToArray();
    }
}
