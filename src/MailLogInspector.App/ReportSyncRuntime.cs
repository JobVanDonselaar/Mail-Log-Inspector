using System.Net.Http;
using MailLogInspector.Core;
using MailLogInspector.Storage;

namespace MailLogInspector.App;

internal sealed class ReportSyncRuntime : IDisposable
{
    private readonly HttpClient _httpClient;

    private ReportSyncRuntime(
        HttpClient httpClient,
        ReportSyncCoordinator coordinator)
    {
        _httpClient = httpClient;
        Coordinator = coordinator;
    }

    public ReportSyncCoordinator Coordinator { get; }

    public static ReportSyncRuntime Create(
        MailLogInspectorWorkspacePaths workspace,
        MailLogInspectorStore mailStore,
        MailLogInspectorImportService importService,
        GmailReportOperationalStore gmailStore,
        SmtpPortalOperationalStore portalStore,
        ReportSyncOperationalStore syncStore)
    {
        var httpClient = new HttpClient();
        var importRunner = new GmailZipImportRunner(importService);
        var gmailService = new GmailReportSyncService(
            gmailStore,
            new GmailOAuthService(httpClient),
            new GmailImapReportClient(),
            new GmailZipDownloader(httpClient),
            importRunner,
            workspace);
        var gmailSource = new GmailReportSyncSource(syncStore, gmailService, gmailConfigStore: gmailStore);
        var directSource = new SmtpPortalReportSyncSource(
            portalStore,
            syncStore,
            mailStore,
            importRunner,
            new SmtpPortalBrowserFactory(),
            workspace);
        return new ReportSyncRuntime(
            httpClient,
            new ReportSyncCoordinator(syncStore, directSource, gmailSource));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}