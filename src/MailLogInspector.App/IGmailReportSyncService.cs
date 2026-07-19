namespace MailLogInspector.App;

public interface IGmailReportSyncService
{
    Task<GmailReportSyncResult> SyncAsync(
        CancellationToken cancellationToken,
        IProgress<string>? progress = null,
        bool latestOnly = false,
        DateTime? minimumReportDayExclusive = null);
}
