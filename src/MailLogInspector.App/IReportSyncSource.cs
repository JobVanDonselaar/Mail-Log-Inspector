namespace MailLogInspector.App;

public interface IReportSyncSource
{
    string SourceLabel { get; }

    Task<ReportSyncSourceResult> SyncAsync(
        bool latestOnly,
        DateTime? minimumReportDayExclusive,
        CancellationToken cancellationToken,
        IProgress<string>? progress = null);
}
