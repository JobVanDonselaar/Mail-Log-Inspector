using MailLogInspector.App;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class SmtpPortalReportMatcherTests
{
    [Fact]
    public void TryParse_AcceptsDeliveredBouncedQueueRawEventStream()
    {
        const string name = "NextGen_2026-07-17(00)_2026-07-18(00) (delivered + bounced + queue) (raw_event_stream)";

        bool parsed = SmtpPortalReportMatcher.TryParse(name, "Ready", out SmtpPortalReport? report);

        Assert.True(parsed);
        Assert.NotNull(report);
        Assert.Equal(new DateTime(2026, 7, 17), report.PeriodStart);
        Assert.Equal(new DateTime(2026, 7, 18), report.PeriodEnd);
    }

    [Theory]
    [InlineData("NextGen_2026-07-17(00)_2026-07-18(00) (spam) (raw_event_stream)", "Ready")]
    [InlineData("NextGen_2026-07-17(00)_2026-07-18(00) (delivered + bounced + queue) (summary)", "Ready")]
    [InlineData("Other_2026-07-17(00)_2026-07-18(00) (delivered + bounced + queue) (raw_event_stream)", "Ready")]
    [InlineData("NextGen_2026-07-17(00)_2026-07-18(00) (delivered + bounced + queue) (raw_event_stream)", "Processing")]
    public void TryParse_RejectsNonMatchingReports(string name, string status)
    {
        Assert.False(SmtpPortalReportMatcher.TryParse(name, status, out _));
    }

    [Fact]
    public void SelectNewest_ReturnsLatestMatchingReadyReport()
    {
        SmtpPortalReport selected = SmtpPortalReportMatcher.SelectNewest(
        [
            new SmtpPortalReportRow(
                "NextGen_2026-07-16(00)_2026-07-17(00) (delivered + bounced + queue) (raw_event_stream)",
                "Ready",
                "row-older"),
            new SmtpPortalReportRow(
                "NextGen_2026-07-17(00)_2026-07-18(00) (delivered + bounced + queue) (raw_event_stream)",
                "Ready",
                "row-newer")
        ]);

        Assert.Equal(new DateTime(2026, 7, 17), selected.PeriodStart);
        Assert.Equal("row-newer", selected.RowKey);
    }
}
