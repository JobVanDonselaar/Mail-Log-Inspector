using System.IO;
using MailLogInspector.Storage;

namespace MailLogInspector.App;

public sealed class SmtpPortalProbeService
{
    private readonly SmtpPortalOperationalStore _operationalStore;
    private readonly MailLogInspectorStore _mailStore;
    private readonly string _incomingDirectory;
    private readonly ISmtpPortalBrowser _browser;
    private readonly Func<DateTime> _todayProvider;
    private readonly Func<string, string> _unprotectSecret;
    private readonly Func<DateTime> _utcNowProvider;

    public SmtpPortalProbeService(
        SmtpPortalOperationalStore operationalStore,
        MailLogInspectorStore mailStore,
        string incomingDirectory,
        ISmtpPortalBrowser browser,
        Func<DateTime>? todayProvider = null,
        Func<string, string>? unprotectSecret = null,
        Func<DateTime>? utcNowProvider = null)
    {
        _operationalStore = operationalStore;
        _mailStore = mailStore;
        _incomingDirectory = Path.GetFullPath(incomingDirectory);
        _browser = browser;
        _todayProvider = todayProvider ?? (() => DateTime.Today);
        _unprotectSecret = unprotectSecret ?? SmtpPortalSecretProtector.Unprotect;
        _utcNowProvider = utcNowProvider ?? (() => DateTime.UtcNow);
    }

    public Task<SmtpPortalProbeResult> DownloadLatestReportAsync(
        SmtpPortalConfig config,
        DateTime? latestSuccessfulReportDay,
        CancellationToken cancellationToken)
    {
        return DownloadLatestReportAsync(
            config,
            latestSuccessfulReportDay,
            visible: false,
            cancellationToken,
            progress: null);
    }

    public async Task<SmtpPortalProbeResult> DownloadLatestReportAsync(
        SmtpPortalConfig config,
        DateTime? latestSuccessfulReportDay,
        bool visible,
        CancellationToken cancellationToken,
        IProgress<string>? progress)
    {
        ValidateConfig(config);
        DateTime attemptedAtUtc = _utcNowProvider();
        bool completed = false;
        try
        {
            progress?.Report("SMTP.com-portaal openen...");
            var credentials = new SmtpPortalCredentials(
                config.Username!.Trim(),
                _unprotectSecret(config.EncryptedPassword!),
                SmtpPortalTotpGenerator.GenerateWindow(
                    _unprotectSecret(config.EncryptedTotpSecret!),
                    DateTimeOffset.UtcNow));

            await _browser.InitializeAsync(credentials, visible, cancellationToken);
            int? requestedPageSize = SmtpPortalPageSizePolicy.Resolve(latestSuccessfulReportDay, _todayProvider());
            if (requestedPageSize.HasValue)
            {
                progress?.Report("Achterstand gevonden; 100 rapporten op pagina 1 tonen...");
                await _browser.SetPageSizeAsync(requestedPageSize.Value, cancellationToken);
            }

            progress?.Report("Gereed dagrapport zoeken...");
            IReadOnlyList<SmtpPortalReportRow> rows = await _browser.ReadFirstPageReportsAsync(cancellationToken);
            DateTime usedAtUtc = _utcNowProvider();
            _operationalStore.RecordSuccessfulPortalUse(usedAtUtc);
            config = config with { LastSuccessfulPortalUseAtUtc = usedAtUtc };
            string reportNameTemplate = SmtpPortalReportNameSyntax.ResolveTemplate(
                config.UseDefaultReportSyntax,
                config.CustomReportSyntax);
            SmtpPortalReport report = SmtpPortalReportMatcher.SelectNewest(rows, reportNameTemplate);

            Directory.CreateDirectory(_incomingDirectory);
            string temporaryDirectory = Path.Combine(_incomingDirectory, ".download");
            Directory.CreateDirectory(temporaryDirectory);
            progress?.Report($"Proefdownload {report.PeriodStart:dd-MM-yyyy}...");
            string temporaryPath = await _browser.DownloadAsync(report, temporaryDirectory, cancellationToken);
            SmtpPortalZipInspection inspection = await SmtpPortalZipValidator.ValidateAsync(temporaryPath, cancellationToken);

            string finalName =
                $"NextGen_{report.PeriodStart:yyyy-MM-dd}_{report.PeriodEnd:yyyy-MM-dd}_{inspection.Sha256[..12]}.zip";
            string finalPath = Path.Combine(_incomingDirectory, finalName);
            if (File.Exists(finalPath))
            {
                if (!string.Equals(Path.GetFullPath(temporaryPath), Path.GetFullPath(finalPath), StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(temporaryPath);
                }
            }
            else
            {
                File.Move(temporaryPath, finalPath);
            }

            bool alreadyImported = _mailStore.HasImportedSourceHashReadOnly(inspection.Sha256);
            var result = new SmtpPortalProbeResult(
                report.Name,
                report.PeriodStart,
                report.PeriodEnd,
                finalPath,
                inspection.Sha256,
                inspection.FileSize,
                alreadyImported);

            _operationalStore.UpsertProbeHistory(new SmtpPortalProbeHistoryRow(
                result.ReportName,
                result.PeriodStart,
                result.PeriodEnd,
                result.Sha256,
                result.LocalPath,
                result.FileSize,
                result.AlreadyImported,
                "ok",
                null,
                attemptedAtUtc));
            _operationalStore.SaveConfig(config with
            {
                ConnectionStatus = alreadyImported
                    ? "Proefdownload geslaagd; bestand was al geïmporteerd"
                    : "Proefdownload geslaagd; nieuw bestand, niet geïmporteerd",
                LastProbeAtUtc = attemptedAtUtc
            });

            completed = true;
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _operationalStore.SaveConfig(config with
            {
                ConnectionStatus = "Proefdownload mislukt: " + ex.Message,
                LastProbeAtUtc = attemptedAtUtc
            });
            throw;
        }
        finally
        {
            if (!visible || completed)
            {
                await _browser.DisposeAsync();
            }
        }
    }

    private static void ValidateConfig(SmtpPortalConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Username) ||
            string.IsNullOrWhiteSpace(config.EncryptedPassword) ||
            string.IsNullOrWhiteSpace(config.EncryptedTotpSecret))
        {
            throw new InvalidOperationException("Vul SMTP.com-gebruikersnaam, wachtwoord en MFA-secret in.");
        }
    }
}
