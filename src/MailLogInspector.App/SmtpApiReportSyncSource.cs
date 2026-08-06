using System.IO;
using MailLogInspector.Core;
using MailLogInspector.Storage;

namespace MailLogInspector.App;

public sealed class SmtpApiReportSyncSource : IReportSyncSource
{
    private readonly SmtpApiOperationalStore _apiStore;
    private readonly ReportSyncOperationalStore _syncStore;
    private readonly MailLogInspectorStore _mailStore;
    private readonly IReportZipImportRunner _importRunner;
    private readonly ISmtpApiClient _apiClient;
    private readonly MailLogInspectorWorkspacePaths _workspace;
    private readonly Func<DateTime> _todayProvider;
    private readonly Func<string, string> _unprotectSecret;
    private readonly Func<DateTime> _utcNowProvider;

    public SmtpApiReportSyncSource(
        SmtpApiOperationalStore apiStore,
        ReportSyncOperationalStore syncStore,
        MailLogInspectorStore mailStore,
        IReportZipImportRunner importRunner,
        ISmtpApiClient apiClient,
        MailLogInspectorWorkspacePaths workspace,
        Func<DateTime>? todayProvider = null,
        Func<string, string>? unprotectSecret = null,
        Func<DateTime>? utcNowProvider = null)
    {
        _apiStore = apiStore;
        _syncStore = syncStore;
        _mailStore = mailStore;
        _importRunner = importRunner;
        _apiClient = apiClient;
        _workspace = workspace;
        _todayProvider = todayProvider ?? (() => DateTime.Today);
        _unprotectSecret = unprotectSecret ?? SmtpPortalSecretProtector.Unprotect;
        _utcNowProvider = utcNowProvider ?? (() => DateTime.UtcNow);
    }

    public string SourceLabel => ReportImportSource.SmtpApi;

    public async Task<ReportSyncSourceResult> SyncAsync(
        bool latestOnly,
        DateTime? minimumReportDayExclusive,
        CancellationToken cancellationToken,
        IProgress<string>? progress = null)
    {
        SmtpApiConfig config = _apiStore.LoadConfig();
        string apiKey = ResolveApiKey(config);
        IReadOnlyList<string> templates = SmtpApiReportSyntaxSet.Resolve(
            config.ReportSyntax1,
            config.ReportSyntax2,
            config.ReportSyntax3);

        progress?.Report($"Bron={SourceLabel} | Beschikbare rapporten opvragen...");
        IReadOnlyList<SmtpApiReport> reports =
            await _apiClient.ListOndemandReportsAsync(apiKey, cancellationToken);
        _apiStore.RecordSuccessfulUse(_utcNowProvider());

        DateTime yesterday = _todayProvider().Date.AddDays(-1);
        IReadOnlyList<SmtpApiSelectedReport> required = SmtpApiReportMatcher.SelectRequired(
            reports,
            templates,
            config.Channel,
            minimumReportDayExclusive,
            yesterday,
            latestOnly);

        if (required.Count == 0)
        {
            return new ReportSyncSourceResult(
                SourceLabel,
                0,
                0,
                0,
                true,
                minimumReportDayExclusive,
                "Geen passend SMTP.com API-dagrapport gevonden.");
        }

        SmtpApiImportOutcome outcome = await ImportReportsAsync(
            required,
            cancellationToken,
            progress);

        DateTime? latestReportDay = outcome.LatestReportDay ?? minimumReportDayExclusive;
        bool missingReadyReport =
            !latestOnly &&
            (!latestReportDay.HasValue || latestReportDay.Value.Date < yesterday);
        return new ReportSyncSourceResult(
            SourceLabel,
            outcome.Imported,
            0,
            outcome.Skipped,
            missingReadyReport,
            latestReportDay,
            missingReadyReport
                ? "Een of meer benodigde API-dagrapporten ontbreken."
                : $"{outcome.Imported} via API geïmporteerd, {outcome.Skipped} overgeslagen.");
    }

    public async Task<IReadOnlyList<SmtpApiSelectedReport>> ListAvailableReportsAsync(
        CancellationToken cancellationToken)
    {
        SmtpApiConfig config = _apiStore.LoadConfig();
        string apiKey = ResolveApiKey(config);
        IReadOnlyList<string> templates = SmtpApiReportSyntaxSet.Resolve(
            config.ReportSyntax1,
            config.ReportSyntax2,
            config.ReportSyntax3);
        IReadOnlyList<SmtpApiReport> reports =
            await _apiClient.ListOndemandReportsAsync(apiKey, cancellationToken);
        _apiStore.RecordSuccessfulUse(_utcNowProvider());
        return SmtpApiReportMatcher.ParseAvailable(reports, templates, config.Channel);
    }

    public async Task<ReportSyncSourceResult> ImportSelectedAsync(
        IReadOnlyList<SmtpApiSelectedReport> selectedReports,
        CancellationToken cancellationToken,
        IProgress<string>? progress = null)
    {
        if (selectedReports.Count == 0)
        {
            return new ReportSyncSourceResult(
                SourceLabel,
                0,
                0,
                0,
                true,
                null,
                "Geen rapporten geselecteerd.");
        }

        SmtpApiImportOutcome outcome = await ImportReportsAsync(
            selectedReports.OrderBy(report => report.PeriodStart).ToArray(),
            cancellationToken,
            progress);
        return new ReportSyncSourceResult(
            SourceLabel,
            outcome.Imported,
            0,
            outcome.Skipped,
            false,
            outcome.LatestReportDay,
            $"{outcome.Imported} via API geïmporteerd, {outcome.Skipped} overgeslagen.");
    }

    private async Task<SmtpApiImportOutcome> ImportReportsAsync(
        IReadOnlyList<SmtpApiSelectedReport> reports,
        CancellationToken cancellationToken,
        IProgress<string>? progress)
    {
        string incomingDirectory = Path.Combine(_workspace.IncomingDirectory, "SmtpApi");
        string temporaryDirectory = Path.Combine(incomingDirectory, ".download");
        Directory.CreateDirectory(temporaryDirectory);

        int imported = 0;
        int skipped = 0;
        DateTime? latestReportDay = null;

        foreach (SmtpApiSelectedReport report in reports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Bron={SourceLabel} | Download {report.PeriodStart:dd-MM-yyyy}...");

            string temporaryPath = Path.Combine(temporaryDirectory, $"{report.ReportId}.zip");
            await _apiClient.DownloadReportAsync(report.Url, temporaryPath, cancellationToken);
            SmtpPortalZipInspection inspection =
                await SmtpPortalZipValidator.ValidateAsync(temporaryPath, cancellationToken);
            string finalPath = MoveToIncoming(
                temporaryPath,
                incomingDirectory,
                report,
                inspection.Sha256);

            if (_mailStore.HasImportedSourceHashReadOnly(inspection.Sha256))
            {
                skipped++;
                latestReportDay = MaxDate(latestReportDay, report.PeriodStart);
                MailLogInspectorLog.Info(
                    "sync",
                    $"Bron={SourceLabel} | Rapport={report.PeriodStart:dd-MM-yyyy} | Reeds geïmporteerd");
                continue;
            }

            GmailZipImportOutcome importOutcome =
                await _importRunner.ImportAsync(finalPath, cancellationToken);
            if (!importOutcome.Success)
            {
                throw new InvalidOperationException(
                    $"Import van API-rapport {report.PeriodStart:dd-MM-yyyy} is mislukt.");
            }

            imported++;
            DateTime reportDay = report.PeriodStart.Date;
            latestReportDay = MaxDate(latestReportDay, reportDay);
            _syncStore.RecordImportSource(new ReportImportSourceRow(
                importOutcome.SourceHash,
                SourceLabel,
                Path.GetFileName(finalPath),
                reportDay,
                _utcNowProvider()));
            MailLogInspectorLog.Info(
                "sync",
                $"Bron={SourceLabel} | Rapport={reportDay:dd-MM-yyyy} | Import geslaagd");
        }

        return new SmtpApiImportOutcome(imported, skipped, latestReportDay);
    }

    private string ResolveApiKey(SmtpApiConfig config)
    {
        if (!config.HasApiKey)
        {
            throw new InvalidOperationException(
                "De SMTP.com API is niet geconfigureerd. Vul een API-sleutel in onder /admin.");
        }

        string apiKey = _unprotectSecret(config.EncryptedApiKey!);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "De opgeslagen SMTP.com API-sleutel kon niet worden gelezen.");
        }

        return apiKey;
    }

    private static string MoveToIncoming(
        string temporaryPath,
        string incomingDirectory,
        SmtpApiSelectedReport report,
        string sourceHash)
    {
        Directory.CreateDirectory(incomingDirectory);
        string finalPath = Path.Combine(
            incomingDirectory,
            $"SmtpApi_{report.PeriodStart:yyyy-MM-dd}_{report.PeriodEnd:yyyy-MM-dd}_{sourceHash[..12]}.zip");
        if (File.Exists(finalPath))
        {
            if (!string.Equals(
                    Path.GetFullPath(temporaryPath),
                    Path.GetFullPath(finalPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(temporaryPath);
            }
        }
        else
        {
            File.Move(temporaryPath, finalPath);
        }

        return finalPath;
    }

    private static DateTime MaxDate(DateTime? current, DateTime candidate)
    {
        return !current.HasValue || candidate.Date > current.Value.Date
            ? candidate.Date
            : current.Value.Date;
    }

    private sealed record SmtpApiImportOutcome(int Imported, int Skipped, DateTime? LatestReportDay);
}
