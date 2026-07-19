using MailLogInspector.Core;
using MailLogInspector.Storage;

namespace MailLogInspector.App;

public sealed class ReportSyncCoordinator
{
    private readonly ReportSyncOperationalStore _store;
    private readonly IReportSyncSource _directSource;
    private readonly IReportSyncSource _gmailSource;
    private readonly Func<DateTime> _utcNow;

    public ReportSyncCoordinator(
        ReportSyncOperationalStore store,
        IReportSyncSource directSource,
        IReportSyncSource gmailSource,
        Func<DateTime>? utcNow = null)
    {
        _store = store;
        _directSource = directSource;
        _gmailSource = gmailSource;
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
            ReportSyncMode.DirectOnly => await RunSourceAsync(
                _directSource,
                latestOnly,
                minimumReportDayExclusive,
                cancellationToken,
                progress),
            ReportSyncMode.DirectWithGmailFallback => await RunDirectWithFallbackAsync(
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

    private async Task<ReportSyncSourceResult> RunDirectWithFallbackAsync(
        bool latestOnly,
        DateTime? minimumReportDayExclusive,
        CancellationToken cancellationToken,
        IProgress<string>? progress)
    {
        try
        {
            ReportSyncSourceResult direct = await RunSourceAsync(
                _directSource,
                latestOnly,
                minimumReportDayExclusive,
                cancellationToken,
                progress);
            if (!direct.NoReadyReport)
            {
                return direct;
            }

            const string reason = "Geen Ready-rapport via SMTP.com direct.";
            MailLogInspectorLog.Info("sync", $"Bron={ReportImportSource.SmtpDirect} | {reason} | Fallback=IMAP");
            progress?.Report(reason + " IMAP wordt geprobeerd...");
            return WithFallbackSummary(
                await RunSourceAsync(
                    _gmailSource,
                    latestOnly,
                    direct.LatestReportDay ?? minimumReportDayExclusive,
                    cancellationToken,
                    progress),
                reason);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            MailLogInspectorLog.Error(
                "sync",
                $"Bron={ReportImportSource.SmtpDirect} | Directe synchronisatie mislukt | Fallback=IMAP",
                ex);
            progress?.Report("Direct downloaden mislukt. IMAP wordt geprobeerd...");
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
        string directReason)
    {
        return result with
        {
            Summary = $"Directe download niet gebruikt ({directReason}); fallback IMAP. {result.Summary}"
        };
    }
}
