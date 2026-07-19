using System.IO.Compression;
using MailLogInspector.App;
using MailLogInspector.Core;
using MailLogInspector.Storage;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class SmtpPortalReportSyncSourceTests
{
    [Fact]
    public async Task EmptyHistoryImportsOnlyNewestReportAndRecordsDirectSource()
    {
        TestContext context = CreateContext();
        string zip = CreateZip(context.Root, "portal.zip");
        var browser = new FakeBrowser(zip, CreateRows(17, 18));
        var importer = new FakeImportRunner();
        var source = context.CreateSource(browser, importer);

        ReportSyncSourceResult result = await source.SyncAsync(
            latestOnly: true,
            minimumReportDayExclusive: null,
            CancellationToken.None);

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(new DateTime(2026, 7, 18), result.LatestReportDay);
        Assert.Single(importer.ImportedPaths);
        ReportImportSourceRow recorded = Assert.Single(context.SyncStore.ReadImportSources(10));
        Assert.Equal(ReportImportSource.SmtpDirect, recorded.Source);
        Assert.Equal("HASH-DIRECT", recorded.SourceHash);
    }

    [Fact]
    public async Task MissingReadyReportReturnsFallbackSignal()
    {
        TestContext context = CreateContext();
        string zip = CreateZip(context.Root, "unused.zip");
        var browser = new FakeBrowser(zip, []);
        var source = context.CreateSource(browser, new FakeImportRunner());

        ReportSyncSourceResult result = await source.SyncAsync(
            latestOnly: false,
            minimumReportDayExclusive: new DateTime(2026, 7, 17),
            CancellationToken.None);

        Assert.True(result.NoReadyReport);
        Assert.Equal(0, result.ImportedCount);
    }

    private static TestContext CreateContext()
    {
        string root = Path.Combine(Path.GetTempPath(), "mail-log-inspector-direct-source-" + Guid.NewGuid().ToString("N"));
        MailLogInspectorWorkspacePaths workspace = MailLogInspectorWorkspaceBootstrapper.Prepare(root);
        var mailStore = new MailLogInspectorStore(workspace.DatabasePath);
        mailStore.Initialize();
        var portalStore = new SmtpPortalOperationalStore(workspace.GmailOperationalDatabasePath);
        portalStore.Initialize();
        portalStore.SaveConfig(new SmtpPortalConfig(
            "user@example.test",
            "password",
            "totp",
            null,
            null));
        var syncStore = new ReportSyncOperationalStore(workspace.GmailOperationalDatabasePath);
        syncStore.Initialize();
        return new TestContext(root, workspace, mailStore, portalStore, syncStore);
    }

    private static IReadOnlyList<SmtpPortalReportRow> CreateRows(params int[] days)
    {
        return days.Select(day => new SmtpPortalReportRow(
            $"NextGen_2026-07-{day:00}(00)_2026-07-{day + 1:00}(00) (delivered + bounced + queue) (raw_event_stream)",
            "Ready",
            "row-" + day)).ToArray();
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
        ReportSyncOperationalStore SyncStore)
    {
        public SmtpPortalReportSyncSource CreateSource(
            ISmtpPortalBrowser browser,
            IReportZipImportRunner importer)
        {
            return new SmtpPortalReportSyncSource(
                PortalStore,
                SyncStore,
                MailStore,
                importer,
                new FakeBrowserFactory(browser),
                Workspace,
                () => new DateTime(2026, 7, 19),
                value => value);
        }
    }

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

        public FakeBrowser(string zip, IReadOnlyList<SmtpPortalReportRow> rows)
        {
            _zip = zip;
            _rows = rows;
        }

        public Task InitializeAsync(SmtpPortalCredentials credentials, bool visible, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SetPageSizeAsync(int pageSize, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<SmtpPortalReportRow>> ReadFirstPageReportsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_rows);

        public Task<string> DownloadAsync(
            SmtpPortalReport report,
            string temporaryDirectory,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(temporaryDirectory);
            string target = Path.Combine(temporaryDirectory, report.PeriodStart.ToString("yyyyMMdd") + ".zip");
            File.Copy(_zip, target);
            return Task.FromResult(target);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeImportRunner : IReportZipImportRunner
    {
        public List<string> ImportedPaths { get; } = [];

        public Task<GmailZipImportOutcome> ImportAsync(string zipPath, CancellationToken cancellationToken)
        {
            ImportedPaths.Add(zipPath);
            return Task.FromResult(new GmailZipImportOutcome(
                true,
                "HASH-DIRECT",
                new DateTime(2026, 7, 18),
                new DateTime(2026, 7, 18, 23, 59, 0),
                false));
        }
    }
}
