using MailLogInspector.App;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class ReportSyncRunPlanTests
{
    [Fact]
    public void EmptyDatabaseDownloadsOnlyLatestReport()
    {
        ReportSyncRunPlan plan = ReportSyncRunPlan.Create(latestSuccessfulReportDay: null);

        Assert.True(plan.LatestOnly);
        Assert.Null(plan.MinimumReportDayExclusive);
    }

    [Fact]
    public void ExistingDatabaseCatchesUpAfterLatestReportDay()
    {
        DateTime latestReportDay = new(2026, 7, 17);

        ReportSyncRunPlan plan = ReportSyncRunPlan.Create(latestReportDay);

        Assert.False(plan.LatestOnly);
        Assert.Equal(latestReportDay, plan.MinimumReportDayExclusive);
    }
}