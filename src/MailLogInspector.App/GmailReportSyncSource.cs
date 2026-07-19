using MailLogInspector.Storage;

namespace MailLogInspector.App;

public sealed class GmailReportSyncSource : IReportSyncSource
{
    private readonly ReportSyncOperationalStore _operationalStore;
    private readonly IGmailReportSyncService _syncService;
    private readonly Func<DateTime> _utcNow;
    private readonly GmailReportOperationalStore? _gmailConfigStore;

    public GmailReportSyncSource(
        ReportSyncOperationalStore operationalStore,
        IGmailReportSyncService syncService,
        Func<DateTime>? utcNow = null,
        GmailReportOperationalStore? gmailConfigStore = null)
    {
        _operationalStore = operationalStore;
        _syncService = syncService;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _gmailConfigStore = gmailConfigStore;
    }

    public string SourceLabel => ResolveSourceLabel();

    public async Task<ReportSyncSourceResult> SyncAsync(
        bool latestOnly,
        DateTime? minimumReportDayExclusive,
        CancellationToken cancellationToken,
        IProgress<string>? progress = null)
    {
        string sourceLabel = ResolveSourceLabel();
        GmailReportSyncResult result = await _syncService.SyncAsync(
            cancellationToken,
            progress,
            latestOnly,
            minimumReportDayExclusive);

        foreach (ReportImportedArtifact artifact in result.ImportedArtifacts)
        {
            if (artifact.AlreadyImported)
            {
                continue;
            }

            _operationalStore.RecordImportSource(new ReportImportSourceRow(
                artifact.SourceHash,
                sourceLabel,
                artifact.FileName,
                artifact.ReportDay,
                _utcNow()));
        }

        DateTime? latestReportDay = result.ImportedArtifacts.Count == 0
            ? minimumReportDayExclusive
            : result.ImportedArtifacts.Max(static artifact => artifact.ReportDay);
        bool noReportFound =
            result.FailedCount == 0 &&
            result.ImportedCount == 0 &&
            result.SkippedCount == 0 &&
            result.ImportedArtifacts.Count == 0;

        return new ReportSyncSourceResult(
            sourceLabel,
            result.ImportedCount,
            result.FailedCount,
            result.SkippedCount,
            noReportFound,
            latestReportDay,
            $"Bron={sourceLabel} | {result.ImportedCount} geimporteerd, " +
            $"{result.SkippedCount} overgeslagen, {result.FailedCount} mislukt.");
    }

    private string ResolveSourceLabel()
    {
        return _gmailConfigStore is null
            ? ReportImportSource.Gmail
            : ReportImportSource.FromImapProvider(_gmailConfigStore.LoadConfig().ImapProvider);
    }
}
