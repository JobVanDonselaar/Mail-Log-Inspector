using System.IO.Compression;
using MailLogInspector.App;
using MailLogInspector.Core;
using MailLogInspector.Storage;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class SmtpPortalConfiguredSyntaxIntegrationTests
{
    private const string CustomTemplate = "Exquise_{start}_{end}_dagrapport";
    private const string CustomName = "Exquise_2026-07-18_2026-07-19_dagrapport";

    [Fact]
    public async Task Probe_UsesConfiguredSyntaxAndRecordsSuccessfulPageRead()
    {
        TestContext context = CreateContext();
        string zip = CreateZip(context.Root, "probe.zip");
        var browser = new FakeBrowser(zip, [new(CustomName, "Ready", "custom")]);
        DateTime usedAtUtc = new(2026, 7, 19, 8, 30, 0, DateTimeKind.Utc);
        var service = new SmtpPortalProbeService(
            context.PortalStore,
            context.MailStore,
            Path.Combine(context.Workspace.IncomingDirectory, "Probe"),
            browser,
            () => new DateTime(2026, 7, 19),
            value => value,
            () => usedAtUtc);

        SmtpPortalProbeResult result = await service.DownloadLatestReportAsync(
            context.Config,
            new DateTime(2026, 7, 18),
            CancellationToken.None);

        Assert.Equal(CustomName, result.ReportName);
        Assert.Equal(usedAtUtc, context.PortalStore.LoadConfig().LastSuccessfulPortalUseAtUtc);
    }

    [Fact]
    public async Task Probe_DoesNotRecordTimestampWhenPageReadFails()
    {
        TestContext context = CreateContext();
        string zip = CreateZip(context.Root, "unused.zip");
        var browser = new FakeBrowser(
            zip,
            [new(CustomName, "Ready", "custom")],
            failRead: true);
        var service = new SmtpPortalProbeService(
            context.PortalStore,
            context.MailStore,
            Path.Combine(context.Workspace.IncomingDirectory, "Probe"),
            browser,
            () => new DateTime(2026, 7, 19),
            value => value,
            () => new DateTime(2026, 7, 19, 8, 30, 0, DateTimeKind.Utc));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DownloadLatestReportAsync(
                context.Config,
                new DateTime(2026, 7, 18),
                CancellationToken.None));

        Assert.Null(context.PortalStore.LoadConfig().LastSuccessfulPortalUseAtUtc);
    }

    [Fact]
    public async Task Probe_RetainsTimestampWhenDownloadFailsAfterPageRead()
    {
        TestContext context = CreateContext();
        string zip = CreateZip(context.Root, "unused.zip");
        var browser = new FakeBrowser(
            zip,
            [new(CustomName, "Ready", "custom")],
            failDownload: true);
        DateTime usedAtUtc = new(2026, 7, 19, 8, 30, 0, DateTimeKind.Utc);
        var service = new SmtpPortalProbeService(
            context.PortalStore,
            context.MailStore,
            Path.Combine(context.Workspace.IncomingDirectory, "Probe"),
            browser,
            () => new DateTime(2026, 7, 19),
            value => value,
            () => usedAtUtc);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DownloadLatestReportAsync(
                context.Config,
                new DateTime(2026, 7, 18),
                CancellationToken.None));

        Assert.Equal(usedAtUtc, context.PortalStore.LoadConfig().LastSuccessfulPortalUseAtUtc);
    }

    [Fact]
    public async Task ProductionSync_UsesConfiguredSyntaxAndRecordsSuccessfulPageRead()
    {
        TestContext context = CreateContext();
        string zip = CreateZip(context.Root, "production.zip");
        var browser = new FakeBrowser(zip, [new(CustomName, "Ready", "custom")]);
        var importer = new FakeImportRunner();
        DateTime usedAtUtc = new(2026, 7, 19, 9, 0, 0, DateTimeKind.Utc);
        var source = new SmtpPortalReportSyncSource(
            context.PortalStore,
            context.SyncStore,
            context.MailStore,
            importer,
            new FakeBrowserFactory(browser),
            context.Workspace,
            () => new DateTime(2026, 7, 19),
            value => value,
            () => usedAtUtc);

        ReportSyncSourceResult result = await source.SyncAsync(
            latestOnly: true,
            minimumReportDayExclusive: null,
            CancellationToken.None);

        Assert.Equal(1, result.ImportedCount);
        Assert.Single(importer.ImportedPaths);
        Assert.Equal(usedAtUtc, context.PortalStore.LoadConfig().LastSuccessfulPortalUseAtUtc);
    }

    private static TestContext CreateContext()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "mail-log-inspector-configured-syntax-" + Guid.NewGuid().ToString("N"));
        MailLogInspectorWorkspacePaths workspace = MailLogInspectorWorkspaceBootstrapper.Prepare(root);
        var mailStore = new MailLogInspectorStore(workspace.DatabasePath);
        mailStore.Initialize();
        var portalStore = new SmtpPortalOperationalStore(workspace.GmailOperationalDatabasePath);
        portalStore.Initialize();
        var config = new SmtpPortalConfig(
            "user@example.test",
            "password",
            "totp",
            null,
            null,
            UseDefaultReportSyntax: false,
            CustomReportSyntax: CustomTemplate);
        portalStore.SaveConfig(config);
        var syncStore = new ReportSyncOperationalStore(workspace.GmailOperationalDatabasePath);
        syncStore.Initialize();
        return new TestContext(root, workspace, mailStore, portalStore, syncStore, config);
    }

    private static string CreateZip(string root, string fileName)
    {
        string path = Path.Combine(root, fileName);
        using FileStream stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        using StreamWriter writer = new(archive.CreateEntry("report.csv").Open());
        writer.Write("header,value\none,1");
        return path;
    }

    private sealed record TestContext(
        string Root,
        MailLogInspectorWorkspacePaths Workspace,
        MailLogInspectorStore MailStore,
        SmtpPortalOperationalStore PortalStore,
        ReportSyncOperationalStore SyncStore,
        SmtpPortalConfig Config);

    private sealed class FakeBrowserFactory : ISmtpPortalBrowserFactory
    {
        private readonly ISmtpPortalBrowser _browser;

        public FakeBrowserFactory(ISmtpPortalBrowser browser)
        {
            _browser = browser;
        }

        public ISmtpPortalBrowser Create(string userDataFolder) => _browser;
    }

    private sealed class FakeBrowser : ISmtpPortalBrowser
    {
        private readonly string _zip;
        private readonly IReadOnlyList<SmtpPortalReportRow> _rows;
        private readonly bool _failRead;
        private readonly bool _failDownload;

        public FakeBrowser(
            string zip,
            IReadOnlyList<SmtpPortalReportRow> rows,
            bool failRead = false,
            bool failDownload = false)
        {
            _zip = zip;
            _rows = rows;
            _failRead = failRead;
            _failDownload = failDownload;
        }

        public Task InitializeAsync(
            SmtpPortalCredentials credentials,
            bool visible,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SetPageSizeAsync(int pageSize, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<SmtpPortalReportRow>> ReadFirstPageReportsAsync(
            CancellationToken cancellationToken)
        {
            return _failRead
                ? throw new InvalidOperationException("Rapportpagina niet leesbaar.")
                : Task.FromResult(_rows);
        }

        public Task<string> DownloadAsync(
            SmtpPortalReport report,
            string temporaryDirectory,
            CancellationToken cancellationToken)
        {
            if (_failDownload)
            {
                throw new InvalidOperationException("Download mislukt.");
            }

            Directory.CreateDirectory(temporaryDirectory);
            string target = Path.Combine(temporaryDirectory, Guid.NewGuid().ToString("N") + ".zip");
            File.Copy(_zip, target);
            return Task.FromResult(target);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeImportRunner : IReportZipImportRunner
    {
        public List<string> ImportedPaths { get; } = [];

        public Task<GmailZipImportOutcome> ImportAsync(
            string zipPath,
            CancellationToken cancellationToken)
        {
            ImportedPaths.Add(zipPath);
            return Task.FromResult(new GmailZipImportOutcome(
                true,
                "HASH-CONFIGURED",
                new DateTime(2026, 7, 18),
                new DateTime(2026, 7, 18, 23, 59, 0),
                false));
        }
    }
}
