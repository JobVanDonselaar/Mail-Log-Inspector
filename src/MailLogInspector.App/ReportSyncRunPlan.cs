namespace MailLogInspector.App;

public sealed record ReportSyncRunPlan(
    bool LatestOnly,
    DateTime? MinimumReportDayExclusive)
{
    public static ReportSyncRunPlan Create(DateTime? latestSuccessfulReportDay)
    {
        return new ReportSyncRunPlan(
            LatestOnly: !latestSuccessfulReportDay.HasValue,
            MinimumReportDayExclusive: latestSuccessfulReportDay?.Date);
    }
}