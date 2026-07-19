using System.IO.Compression;
using MailLogInspector.App;
using MailLogInspector.Core;
using MailLogInspector.Storage;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class SmtpPortalProbeServiceTests
{
    [Fact]
    public async Task DownloadLatestReportAsync_LeavesPageSizeUntouchedAndMarksKnownHash()
    {
        TestContext context = CreateContext();
        string knownZip = CreateZip(context.Root, "known.zip");
        string knownHash = (await SmtpPortalZipValidator.ValidateAsync(knownZip, CancellationToken.None)).Sha256;
        InsertImport(context.Workspace.DatabasePath, knownHash);
        var browser = new FakeSmtpPortalBrowser(knownZip);
        var service = context.CreateService(browser);

        SmtpPortalProbeResult result = await service.DownloadLatestReportAsync(
            context.Config,
            new DateTime(2026, 7, 17),
            CancellationToken.None);

        Assert.Null(browser.RequestedPageSize);
        Assert.True(result.AlreadyImported);
        Assert.Equal(new DateTime(2026, 7, 17), result.PeriodStart);
        Assert.True(File.Exists(result.LocalPath));
        Assert.Single(context.PortalStore.ReadProbeHistory(10));
    }

    [Fact]
    public async Task DownloadLatestReportAsync_RequestsHundredForOlderImport()
    {
        TestContext context = CreateContext();
        string zip = CreateZip(context.Root, "new.zip");
        var browser = new FakeSmtpPortalBrowser(zip);
        var service = context.CreateService(browser);

        await service.DownloadLatestReportAsync(
            context.Config,
            new DateTime(2026, 7, 14),
            CancellationToken.None);

        Assert.Equal(100, browser.RequestedPageSize);
    }

    [Fact]
    public async Task DownloadLatestReportAsync_KeepsVisibleDiagnosisOpenAfterFailure()
    {
        TestContext context = CreateContext();
        string zip = CreateZip(context.Root, "failed-visible.zip");
        var browser = new FakeSmtpPortalBrowser(zip, failInitialization: true);
        var service = context.CreateService(browser);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadLatestReportAsync(
            context.Config,
            new DateTime(2026, 7, 17),
            visible: true,
            CancellationToken.None,
            progress: null));

        Assert.False(browser.IsDisposed);
    }

    [Fact]
    public async Task DownloadLatestReportAsync_DisposesHiddenBrowserAfterFailure()
    {
        TestContext context = CreateContext();
        string zip = CreateZip(context.Root, "failed-hidden.zip");
        var browser = new FakeSmtpPortalBrowser(zip, failInitialization: true);
        var service = context.CreateService(browser);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadLatestReportAsync(
            context.Config,
            new DateTime(2026, 7, 17),
            CancellationToken.None));

        Assert.True(browser.IsDisposed);
    }

    private static TestContext CreateContext()
    {
        string root = Path.Combine(Path.GetTempPath(), "mail-log-inspector-portal-probe-" + Guid.NewGuid().ToString("N"));
        MailLogInspectorWorkspacePaths workspace = MailLogInspectorWorkspaceBootstrapper.Prepare(root);
        new MailLogInspectorStore(workspace.DatabasePath).Initialize();
        var portalStore = new SmtpPortalOperationalStore(workspace.GmailOperationalDatabasePath);
        portalStore.Initialize();
        return new TestContext(
            root,
            workspace,
            portalStore,
            new SmtpPortalConfig("user@example.test", "encrypted-password", "encrypted-totp", null, null));
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

    private static void InsertImport(string databasePath, string sourceHash)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO imports (
                source_path, source_file_name, source_hash, imported_at,
                report_start, report_end, row_count,
                delivered_count, bounce_count, underway_count, archive_path
            )
            VALUES (
                'known.zip', 'known.zip', $hash, $importedAt,
                $start, $end, 1, 1, 0, 0, NULL
            );
            """;
        command.Parameters.AddWithValue("$hash", sourceHash);
        command.Parameters.AddWithValue("$importedAt", DateTime.UtcNow);
        command.Parameters.AddWithValue("$start", new DateTime(2026, 7, 17));
        command.Parameters.AddWithValue("$end", new DateTime(2026, 7, 17, 23, 59, 0));
        command.ExecuteNonQuery();
    }

    private sealed record TestContext(
        string Root,
        MailLogInspectorWorkspacePaths Workspace,
        SmtpPortalOperationalStore PortalStore,
        SmtpPortalConfig Config)
    {
        public SmtpPortalProbeService CreateService(ISmtpPortalBrowser browser)
        {
            return new SmtpPortalProbeService(
                PortalStore,
                new MailLogInspectorStore(Workspace.DatabasePath),
                Path.Combine(Workspace.IncomingDirectory, "SmtpPortalProbe"),
                browser,
                () => new DateTime(2026, 7, 18),
                value => value);
        }
    }

    private sealed class FakeSmtpPortalBrowser : ISmtpPortalBrowser
    {
        private readonly string _zipPath;
        private readonly bool _failReportRead;
        private readonly bool _failInitialization;

        public FakeSmtpPortalBrowser(
            string zipPath,
            bool failReportRead = false,
            bool failInitialization = false)
        {
            _zipPath = zipPath;
            _failReportRead = failReportRead;
            _failInitialization = failInitialization;
        }

        public int? RequestedPageSize { get; private set; }
        public bool IsDisposed { get; private set; }

        public Task InitializeAsync(SmtpPortalCredentials credentials, bool visible, CancellationToken cancellationToken)
        {
            if (_failInitialization)
            {
                throw new InvalidOperationException("Initialisatiefout.");
            }

            return Task.CompletedTask;
        }

        public Task SetPageSizeAsync(int pageSize, CancellationToken cancellationToken)
        {
            RequestedPageSize = pageSize;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SmtpPortalReportRow>> ReadFirstPageReportsAsync(CancellationToken cancellationToken)
        {
            if (_failReportRead)
            {
                throw new InvalidOperationException("Diagnosefout.");
            }

            IReadOnlyList<SmtpPortalReportRow> rows =
            [
                new(
                    "NextGen_2026-07-17(00)_2026-07-18(00) (delivered + bounced + queue) (raw_event_stream)",
                    "Ready",
                    "latest"),
                new(
                    "NextGen_2026-07-16(00)_2026-07-17(00) (delivered + bounced + queue) (raw_event_stream)",
                    "Ready",
                    "older")
            ];
            return Task.FromResult(rows);
        }

        public Task<string> DownloadAsync(SmtpPortalReport report, string temporaryDirectory, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(temporaryDirectory);
            string target = Path.Combine(temporaryDirectory, Guid.NewGuid().ToString("N") + ".zip");
            File.Copy(_zipPath, target);
            return Task.FromResult(target);
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
