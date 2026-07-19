using System.IO;
using MailLogInspector.Core;
using MailLogInspector.Storage;

namespace MailLogInspector.App;

public sealed class SmtpPortalReportSyncSource : IReportSyncSource
{
    private readonly SmtpPortalOperationalStore _portalStore;
    private readonly ReportSyncOperationalStore _syncStore;
    private readonly MailLogInspectorStore _mailStore;
    private readonly IReportZipImportRunner _importRunner;
    private readonly ISmtpPortalBrowserFactory _browserFactory;
    private readonly MailLogInspectorWorkspacePaths _workspace;
    private readonly Func<DateTime> _todayProvider;
    private readonly Func<string, string> _unprotectSecret;
    private readonly Func<DateTime> _utcNowProvider;

    public SmtpPortalReportSyncSource(
        SmtpPortalOperationalStore portalStore,
        ReportSyncOperationalStore syncStore,
        MailLogInspectorStore mailStore,
        IReportZipImportRunner importRunner,
        ISmtpPortalBrowserFactory browserFactory,
        MailLogInspectorWorkspacePaths workspace,
        Func<DateTime>? todayProvider = null,
        Func<string, string>? unprotectSecret = null,
        Func<DateTime>? utcNowProvider = null)
    {
        _portalStore = portalStore;
        _syncStore = syncStore;
        _mailStore = mailStore;
        _importRunner = importRunner;
        _browserFactory = browserFactory;
        _workspace = workspace;
        _todayProvider = todayProvider ?? (() => DateTime.Today);
        _unprotectSecret = unprotectSecret ?? SmtpPortalSecretProtector.Unprotect;
        _utcNowProvider = utcNowProvider ?? (() => DateTime.UtcNow);
    }

    public string SourceLabel => ReportImportSource.SmtpDirect;

    public async Task<ReportSyncSourceResult> SyncAsync(
        bool latestOnly,
        DateTime? minimumReportDayExclusive,
        CancellationToken cancellationToken,
        IProgress<string>? progress = null)
    {
        SmtpPortalConfig config = _portalStore.LoadConfig();
        ValidateConfig(config);
        string userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Mail Log Inspector",
            "WebView2",
            "SmtpPortal");
        await using ISmtpPortalBrowser browser = _browserFactory.Create(userDataFolder);
        var credentials = new SmtpPortalCredentials(
            config.Username!.Trim(),
            _unprotectSecret(config.EncryptedPassword!),
            SmtpPortalTotpGenerator.GenerateWindow(
                _unprotectSecret(config.EncryptedTotpSecret!),
                DateTimeOffset.UtcNow));

        progress?.Report("SMTP.com direct openen...");
        await browser.InitializeAsync(credentials, visible: false, cancellationToken);
        int? requestedPageSize = SmtpPortalPageSizePolicy.Resolve(
            minimumReportDayExclusive,
            _todayProvider());
        if (requestedPageSize.HasValue)
        {
            await browser.SetPageSizeAsync(requestedPageSize.Value, cancellationToken);
        }

        IReadOnlyList<SmtpPortalReportRow> rows =
            await browser.ReadFirstPageReportsAsync(cancellationToken);
        DateTime usedAtUtc = _utcNowProvider();
        _portalStore.RecordSuccessfulPortalUse(usedAtUtc);
        string reportNameTemplate = SmtpPortalReportNameSyntax.ResolveTemplate(
            config.UseDefaultReportSyntax,
            config.CustomReportSyntax);
        DateTime yesterday = _todayProvider().Date.AddDays(-1);
        IReadOnlyList<SmtpPortalReport> reports = SmtpPortalReportMatcher.SelectRequired(
            rows,
            minimumReportDayExclusive,
            yesterday,
            latestOnly,
            reportNameTemplate);
        if (reports.Count == 0)
        {
            return new ReportSyncSourceResult(
                SourceLabel,
                0,
                0,
                0,
                true,
                minimumReportDayExclusive,
                "Geen gereed SMTP.com-dagrapport gevonden.");
        }

        string incomingDirectory = Path.Combine(_workspace.IncomingDirectory, "SmtpPortal");
        string temporaryDirectory = Path.Combine(incomingDirectory, ".download");
        Directory.CreateDirectory(temporaryDirectory);
        int imported = 0;
        int skipped = 0;
        DateTime? latestReportDay = minimumReportDayExclusive;

        foreach (SmtpPortalReport report in reports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Bron={SourceLabel} | Download {report.PeriodStart:dd-MM-yyyy}...");
            string temporaryPath = await browser.DownloadAsync(
                report,
                temporaryDirectory,
                cancellationToken);
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

            GmailZipImportOutcome outcome =
                await _importRunner.ImportAsync(finalPath, cancellationToken);
            if (!outcome.Success)
            {
                throw new InvalidOperationException(
                    $"Import van SMTP.com-rapport {report.PeriodStart:dd-MM-yyyy} is mislukt.");
            }

            imported++;
            DateTime reportDay = (outcome.ReportStart ?? report.PeriodStart).Date;
            latestReportDay = MaxDate(latestReportDay, reportDay);
            _syncStore.RecordImportSource(new ReportImportSourceRow(
                outcome.SourceHash,
                SourceLabel,
                Path.GetFileName(finalPath),
                reportDay,
                DateTime.UtcNow));
            MailLogInspectorLog.Info(
                "sync",
                $"Bron={SourceLabel} | Rapport={reportDay:dd-MM-yyyy} | Import geslaagd");
        }

        bool missingReadyReport =
            !latestOnly &&
            (!latestReportDay.HasValue || latestReportDay.Value.Date < yesterday);
        return new ReportSyncSourceResult(
            SourceLabel,
            imported,
            0,
            skipped,
            missingReadyReport,
            latestReportDay,
            missingReadyReport
                ? "Een of meer benodigde Ready-rapporten ontbreken."
                : $"{imported} direct geïmporteerd, {skipped} overgeslagen.");
    }

    private static string MoveToIncoming(
        string temporaryPath,
        string incomingDirectory,
        SmtpPortalReport report,
        string sourceHash)
    {
        Directory.CreateDirectory(incomingDirectory);
        string finalPath = Path.Combine(
            incomingDirectory,
            $"NextGen_{report.PeriodStart:yyyy-MM-dd}_{report.PeriodEnd:yyyy-MM-dd}_{sourceHash[..12]}.zip");
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

    private static void ValidateConfig(SmtpPortalConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Username) ||
            string.IsNullOrWhiteSpace(config.EncryptedPassword) ||
            string.IsNullOrWhiteSpace(config.EncryptedTotpSecret))
        {
            throw new InvalidOperationException(
                "SMTP.com direct is niet volledig geconfigureerd.");
        }
    }
}
