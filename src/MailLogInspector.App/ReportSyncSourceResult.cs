namespace MailLogInspector.App;

public sealed record ReportSyncSourceResult(
    string Source,
    int ImportedCount,
    int FailedCount,
    int SkippedCount,
    bool NoReadyReport,
    DateTime? LatestReportDay,
    string Summary)
{
    public bool IsSuccessful => !NoReadyReport && FailedCount == 0;
}
