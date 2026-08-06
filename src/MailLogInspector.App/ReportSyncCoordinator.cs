using MailLogInspector.Core;
using MailLogInspector.Storage;

namespace MailLogInspector.App;

public sealed class ReportSyncCoordinator
{
    private readonly ReportSyncOperationalStore _store;
    private readonly IReportSyncSource _directSource;
    private readonly IReportSyncSource _gmailSource;
    private readonly IReportSyncSource _apiSource;
    private readonly Func<DateTime> _utcNow;

    public ReportSyncCoordinator(
        ReportSyncOperationalStore store,
        IReportSyncSource directSource,
        IReportSyncSource gmailSource,
        IReportSyncSource apiSource,
        Func<DateTime>? utcNow = null)
    {
        _store = store;
        _directSource = directSource;
        _gmailSource = gmailSource;
        _apiSource = apiSource;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public async Task<ReportSyncSourceResult> RunAsync(
        string mode,
        bool latestOnly,
        DateTime? minimumReportDayExclusive,
        CancellationToken cancellationToken,
        IProgress<string>? progress = null)
    {
        string normalizedMode = ReportSyncMode.Normalize(mode);
        DateTime attemptedAtUtc = _utcNow();
        _store.RecordAttempt(normalizedMode, attemptedAtUtc);

        ReportSyncSourceResult result = normalizedMode switch
        {
            ReportSyncMode.ApiOnly => await RunSourceAsync(
                _apiSource,
                latestOnly,
                minimumReportDayExclusive,
                cancellationToken,
                progress),
            ReportSyncMode.ApiWithImapFallback => await RunWithFallbackAsync(
                _apiSource,
                latestOnly,
                minimumReportDayExclusive,
                cancellationToken,
                progress),
            ReportSyncMode.DirectOnly => await RunSourceAsync(
                _directSource,
                latestOnly,
                minimumReportDayExclusive,
                cancellationToken,
                progress),
            ReportSyncMode.DirectWithGmailFallback => await RunWithFallbackAsync(
                _directSource,
                latestOnly,
                minimumReportDayExclusive,
                cancellationToken,
                progress),
            _ => await RunSourceAsync(
                _gmailSource,
                latestOnly,
                minimumReportDayExclusive,
                cancellationToken,
                progress)
        };

        if (result.IsSuccessful)
        {
            _store.RecordSuccess(_utcNow());
        }

        return result;
    }

    private async Task<ReportSyncSourceResult> RunWithFallbackAsync(
        IReportSyncSource primarySource,
        bool latestOnly,
        DateTime? minimumReportDayExclusive,
        CancellationToken cancellationToken,
        IProgress<string>? progress)
    {
        string primaryLabel = primarySource.SourceLabel;
        try
        {
            ReportSyncSourceResult primary = await RunSourceAsync(
                primarySource,
                latestOnly,
                minimumReportDayExclusive,
                cancellationToken,
                progress);
            if (!primary.NoReadyReport)
            {
                return primary;
            }

            string reason = $"Geen Ready-rapport via {primaryLabel}.";
            MailLogInspectorLog.Info("sync", $"Bron={primaryLabel} | {reason} | Fallback=IMAP");
            progress?.Report(reason + " IMAP wordt geprobeerd...");
            return WithFallbackSummary(
                await RunSourceAsync(
                    _gmailSource,
                    latestOnly,
                    primary.LatestReportDay ?? minimumReportDayExclusive,
                    cancellationToken,
                    progress),
                reason);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            MailLogInspectorLog.Error(
                "sync",
                $"Bron={primaryLabel} | Primaire synchronisatie mislukt | Fallback=IMAP",
                ex);
            progress?.Report($"{primaryLabel} mislukt. IMAP wordt geprobeerd...");
            return WithFallbackSummary(
                await RunSourceAsync(
                    _gmailSource,
                    latestOnly,
                    minimumReportDayExclusive,
                    cancellationToken,
                    progress),
                ex.Message);
        }
    }

    private static async Task<ReportSyncSourceResult> RunSourceAsync(
        IReportSyncSource source,
        bool latestOnly,
        DateTime? minimumReportDayExclusive,
        CancellationToken cancellationToken,
        IProgress<string>? progress)
    {
        MailLogInspectorLog.Info("sync", $"Bron={source.SourceLabel} | Synchronisatie gestart");
        ReportSyncSourceResult result = await source.SyncAsync(
            latestOnly,
            minimumReportDayExclusive,
            cancellationToken,
            progress);
        MailLogInspectorLog.Info(
            "sync",
            $"Bron={source.SourceLabel} | Geïmporteerd={result.ImportedCount} | Mislukt={result.FailedCount} | Overgeslagen={result.SkippedCount}");
        return result;
    }

    private static ReportSyncSourceResult WithFallbackSummary(
        ReportSyncSourceResult result,
        string primaryReason)
    {
        return result with
        {
            Summary = $"Primaire bron niet gebruikt ({primaryReason}); fallback IMAP. {result.Summary}"
        };
    }
}
