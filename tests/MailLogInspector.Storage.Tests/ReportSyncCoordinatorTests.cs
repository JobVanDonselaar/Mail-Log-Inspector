using MailLogInspector.App;
using MailLogInspector.Storage;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class ReportSyncCoordinatorTests
{
    [Fact]
    public async Task GmailOnlyCallsOnlyGmail()
    {
        TestContext context = CreateContext();
        var direct = new FakeSource(ReportImportSource.SmtpDirect, Success(ReportImportSource.SmtpDirect));
        var gmail = new FakeSource(ReportImportSource.Gmail, Success(ReportImportSource.Gmail));
        var api = new FakeSource(ReportImportSource.SmtpApi, Success(ReportImportSource.SmtpApi));
        var coordinator = new ReportSyncCoordinator(context.Store, direct, gmail, api, context.UtcNow);

        ReportSyncSourceResult result = await coordinator.RunAsync(
            ReportSyncMode.GmailOnly,
            latestOnly: false,
            minimumReportDayExclusive: null,
            CancellationToken.None);

        Assert.Equal(0, direct.CallCount);
        Assert.Equal(1, gmail.CallCount);
        Assert.Equal(ReportImportSource.Gmail, result.Source);
    }

    [Fact]
    public async Task DirectOnlyNeverCallsGmail()
    {
        TestContext context = CreateContext();
        var direct = new FakeSource(ReportImportSource.SmtpDirect, Success(ReportImportSource.SmtpDirect));
        var gmail = new FakeSource(ReportImportSource.Gmail, Success(ReportImportSource.Gmail));
        var api = new FakeSource(ReportImportSource.SmtpApi, Success(ReportImportSource.SmtpApi));
        var coordinator = new ReportSyncCoordinator(context.Store, direct, gmail, api, context.UtcNow);

        ReportSyncSourceResult result = await coordinator.RunAsync(
            ReportSyncMode.DirectOnly,
            latestOnly: true,
            minimumReportDayExclusive: null,
            CancellationToken.None);

        Assert.Equal(1, direct.CallCount);
        Assert.Equal(0, gmail.CallCount);
        Assert.Equal(ReportImportSource.SmtpDirect, result.Source);
        Assert.True(direct.LastLatestOnly);
    }

    [Fact]
    public async Task FallbackModeCallsGmailAfterDirectException()
    {
        TestContext context = CreateContext();
        var direct = new FakeSource(ReportImportSource.SmtpDirect, new InvalidOperationException("portal failed"));
        var gmail = new FakeSource(ReportImportSource.Gmail, Success(ReportImportSource.Gmail));
        var api = new FakeSource(ReportImportSource.SmtpApi, Success(ReportImportSource.SmtpApi));
        var coordinator = new ReportSyncCoordinator(context.Store, direct, gmail, api, context.UtcNow);

        ReportSyncSourceResult result = await coordinator.RunAsync(
            ReportSyncMode.DirectWithGmailFallback,
            latestOnly: false,
            minimumReportDayExclusive: new DateTime(2026, 7, 17),
            CancellationToken.None);

        Assert.Equal(1, direct.CallCount);
        Assert.Equal(1, gmail.CallCount);
        Assert.Equal(ReportImportSource.Gmail, result.Source);
        Assert.Contains("fallback", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FallbackModeCallsGmailWhenNoReadyReportExists()
    {
        TestContext context = CreateContext();
        var direct = new FakeSource(
            ReportImportSource.SmtpDirect,
            new ReportSyncSourceResult(
                ReportImportSource.SmtpDirect,
                0,
                0,
                0,
                true,
                null,
                "Geen Ready-rapport."));
        var gmail = new FakeSource(ReportImportSource.Gmail, Success(ReportImportSource.Gmail));
        var api = new FakeSource(ReportImportSource.SmtpApi, Success(ReportImportSource.SmtpApi));
        var coordinator = new ReportSyncCoordinator(context.Store, direct, gmail, api, context.UtcNow);

        ReportSyncSourceResult result = await coordinator.RunAsync(
            ReportSyncMode.DirectWithGmailFallback,
            latestOnly: false,
            minimumReportDayExclusive: null,
            CancellationToken.None);

        Assert.Equal(1, gmail.CallCount);
        Assert.Equal(ReportImportSource.Gmail, result.Source);
    }

    [Fact]
    public async Task ApiOnlyCallsOnlyApi()
    {
        TestContext context = CreateContext();
        var direct = new FakeSource(ReportImportSource.SmtpDirect, Success(ReportImportSource.SmtpDirect));
        var gmail = new FakeSource(ReportImportSource.Gmail, Success(ReportImportSource.Gmail));
        var api = new FakeSource(ReportImportSource.SmtpApi, Success(ReportImportSource.SmtpApi));
        var coordinator = new ReportSyncCoordinator(context.Store, direct, gmail, api, context.UtcNow);

        ReportSyncSourceResult result = await coordinator.RunAsync(
            ReportSyncMode.ApiOnly,
            latestOnly: false,
            minimumReportDayExclusive: null,
            CancellationToken.None);

        Assert.Equal(1, api.CallCount);
        Assert.Equal(0, direct.CallCount);
        Assert.Equal(0, gmail.CallCount);
        Assert.Equal(ReportImportSource.SmtpApi, result.Source);
    }

    [Fact]
    public async Task ApiFallbackModeCallsGmailAfterApiException()
    {
        TestContext context = CreateContext();
        var direct = new FakeSource(ReportImportSource.SmtpDirect, Success(ReportImportSource.SmtpDirect));
        var gmail = new FakeSource(ReportImportSource.Gmail, Success(ReportImportSource.Gmail));
        var api = new FakeSource(ReportImportSource.SmtpApi, new InvalidOperationException("api failed"));
        var coordinator = new ReportSyncCoordinator(context.Store, direct, gmail, api, context.UtcNow);

        ReportSyncSourceResult result = await coordinator.RunAsync(
            ReportSyncMode.ApiWithImapFallback,
            latestOnly: false,
            minimumReportDayExclusive: null,
            CancellationToken.None);

        Assert.Equal(1, api.CallCount);
        Assert.Equal(1, gmail.CallCount);
        Assert.Equal(0, direct.CallCount);
        Assert.Equal(ReportImportSource.Gmail, result.Source);
        Assert.Contains("fallback", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownModeNormalizesToApiOnly()
    {
        Assert.Equal(ReportSyncMode.ApiOnly, ReportSyncMode.Normalize("onbekend"));
        Assert.Equal(ReportSyncMode.ApiOnly, ReportSyncMode.Default);
    }

    private static ReportSyncSourceResult Success(string source)
    {
        return new ReportSyncSourceResult(
            source,
            1,
            0,
            0,
            false,
            new DateTime(2026, 7, 18),
            "Import geslaagd.");
    }

    private static TestContext CreateContext()
    {
        string root = Path.Combine(Path.GetTempPath(), "mail-log-inspector-sync-coordinator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new ReportSyncOperationalStore(Path.Combine(root, "operational.sqlite"));
        store.Initialize();
        return new TestContext(store, () => new DateTime(2026, 7, 19, 1, 0, 0, DateTimeKind.Utc));
    }

    private sealed record TestContext(ReportSyncOperationalStore Store, Func<DateTime> UtcNow);

    private sealed class FakeSource : IReportSyncSource
    {
        private readonly ReportSyncSourceResult? _result;
        private readonly Exception? _exception;

        public FakeSource(string sourceLabel, ReportSyncSourceResult result)
        {
            SourceLabel = sourceLabel;
            _result = result;
        }

        public FakeSource(string sourceLabel, Exception exception)
        {
            SourceLabel = sourceLabel;
            _exception = exception;
        }

        public string SourceLabel { get; }
        public int CallCount { get; private set; }
        public bool LastLatestOnly { get; private set; }

        public Task<ReportSyncSourceResult> SyncAsync(
            bool latestOnly,
            DateTime? minimumReportDayExclusive,
            CancellationToken cancellationToken,
            IProgress<string>? progress = null)
        {
            CallCount++;
            LastLatestOnly = latestOnly;
            return _exception is not null
                ? Task.FromException<ReportSyncSourceResult>(_exception)
                : Task.FromResult(_result!);
        }
    }
}
