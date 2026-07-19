using MailLogInspector.Core;
using MailLogInspector.Storage;

namespace MailLogInspector.App;

internal sealed class AdminReportSyncRunner
{
    private readonly MailLogInspectorWorkspacePaths _workspace;
    private readonly GmailReportOperationalStore _gmailStore;
    private readonly SmtpPortalOperationalStore _portalStore;
    private readonly ReportSyncOperationalStore _syncStore;

    public AdminReportSyncRunner(
        MailLogInspectorWorkspacePaths workspace,
        GmailReportOperationalStore gmailStore,
        SmtpPortalOperationalStore portalStore,
        ReportSyncOperationalStore syncStore)
    {
        _workspace = workspace;
        _gmailStore = gmailStore;
        _portalStore = portalStore;
        _syncStore = syncStore;
    }

    public async Task<ReportSyncSourceResult> RunAsync(
        CancellationToken cancellationToken,
        IProgress<string>? progress = null)
    {
        progress?.Report("Database voorbereiden...");
        var rebuildProgress = new Progress<MailLogInspectorImportProgress>(value =>
            progress?.Report(value.Message));
        var rebuilder = new MailLogInspectorWorkspaceRebuilder(_workspace);
        await Task.Run(
            () => rebuilder.RebuildIfRequiredAsync(cancellationToken, rebuildProgress),
            cancellationToken);

        var mailStore = new MailLogInspectorStore(_workspace.DatabasePath);
        mailStore.Initialize();
        var importService = new MailLogInspectorImportService(mailStore, _workspace);
        using ReportSyncRuntime runtime = ReportSyncRuntime.Create(
            _workspace,
            mailStore,
            importService,
            _gmailStore,
            _portalStore,
            _syncStore);

        DateTime? latestReportDay = mailStore.ReadLatestDailyImportReportDayReadOnly();
        ReportSyncRunPlan plan = ReportSyncRunPlan.Create(latestReportDay);
        ReportSyncConfig config = _syncStore.LoadConfig();
        progress?.Report(plan.LatestOnly
            ? "Lege database: nieuwste rapport downloaden en importeren..."
            : "Ontbrekende rapporten downloaden en importeren...");
        return await runtime.Coordinator.RunAsync(
            config.Mode,
            plan.LatestOnly,
            plan.MinimumReportDayExclusive,
            cancellationToken,
            progress);
    }
}