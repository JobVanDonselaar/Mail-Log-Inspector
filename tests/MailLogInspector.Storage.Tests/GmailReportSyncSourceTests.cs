using MailLogInspector.App;
using MailLogInspector.Storage;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class GmailReportSyncSourceTests
{
    [Fact]
    public async Task RecordsGmailAsSourceForImportedArtifact()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "mail-log-inspector-gmail-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new ReportSyncOperationalStore(Path.Combine(root, "operational.sqlite"));
        store.Initialize();
        DateTime importedAtUtc = new(2026, 7, 19, 1, 5, 0, DateTimeKind.Utc);
        var service = new FakeGmailReportSyncService(new GmailReportSyncResult(
            1,
            0,
            0,
            1,
            [new ReportImportedArtifact(
                "HASH-GMAIL",
                "NextGen_2026-07-18.zip",
                new DateTime(2026, 7, 18),
                false)]));
        var source = new GmailReportSyncSource(store, service, () => importedAtUtc);

        ReportSyncSourceResult result = await source.SyncAsync(
            latestOnly: true,
            minimumReportDayExclusive: null,
            CancellationToken.None);

        ReportImportSourceRow row = Assert.Single(store.ReadImportSources(10));
        Assert.Equal(ReportImportSource.Gmail, result.Source);
        Assert.Equal(new DateTime(2026, 7, 18), result.LatestReportDay);
        Assert.Equal(ReportImportSource.Gmail, row.Source);
        Assert.Equal("HASH-GMAIL", row.SourceHash);
        Assert.Equal(importedAtUtc, row.RecordedAtUtc);
        Assert.True(service.LatestOnly);
    }

    [Fact]
    public async Task DoesNotRelabelAnAlreadyImportedArtifactAsGmail()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "mail-log-inspector-gmail-duplicate-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new ReportSyncOperationalStore(Path.Combine(root, "operational.sqlite"));
        store.Initialize();
        var service = new FakeGmailReportSyncService(new GmailReportSyncResult(
            1,
            0,
            0,
            0,
            [new ReportImportedArtifact(
                "HASH-EXISTING",
                "existing.zip",
                new DateTime(2026, 7, 18),
                true)]));
        var source = new GmailReportSyncSource(store, service);

        await source.SyncAsync(
            latestOnly: true,
            minimumReportDayExclusive: null,
            CancellationToken.None);

        Assert.Empty(store.ReadImportSources(10));
    }

    [Fact]
    public async Task RecordsImapForANonGmailMailboxProfile()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "mail-log-inspector-imap-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var syncStore = new ReportSyncOperationalStore(Path.Combine(root, "sync.sqlite"));
        syncStore.Initialize();
        var gmailStore = new GmailReportOperationalStore(Path.Combine(root, "gmail.sqlite"));
        gmailStore.Initialize();
        gmailStore.SaveConfig(GmailReportConfig.Empty with
        {
            AccountEmailAddress = "reports@example.test",
            ImapProvider = ImapProvider.Microsoft365
        });
        var service = new FakeGmailReportSyncService(new GmailReportSyncResult(
            1,
            0,
            0,
            1,
            [new ReportImportedArtifact(
                "HASH-IMAP",
                "NextGen_2026-07-18.zip",
                new DateTime(2026, 7, 18),
                false)]));
        var source = new GmailReportSyncSource(syncStore, service, gmailConfigStore: gmailStore);

        ReportSyncSourceResult result = await source.SyncAsync(
            latestOnly: true,
            minimumReportDayExclusive: null,
            CancellationToken.None);

        Assert.Equal(ReportImportSource.Imap, result.Source);
        Assert.Equal(ReportImportSource.Imap, Assert.Single(syncStore.ReadImportSources(10)).Source);
    }

    [Fact]
    public async Task FailedMessagesAreNotReportedAsMissingReadyReport()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "mail-log-inspector-imap-failure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new ReportSyncOperationalStore(Path.Combine(root, "sync.sqlite"));
        store.Initialize();
        var service = new FakeGmailReportSyncService(new GmailReportSyncResult(
            0,
            1,
            0,
            0,
            []));
        var source = new GmailReportSyncSource(store, service);

        ReportSyncSourceResult result = await source.SyncAsync(
            latestOnly: false,
            minimumReportDayExclusive: new DateTime(2026, 7, 17),
            CancellationToken.None);

        Assert.False(result.NoReadyReport);
        Assert.False(result.IsSuccessful);
    }
    private sealed class FakeGmailReportSyncService : IGmailReportSyncService
    {
        private readonly GmailReportSyncResult _result;

        public FakeGmailReportSyncService(GmailReportSyncResult result)
        {
            _result = result;
        }

        public bool LatestOnly { get; private set; }

        public Task<GmailReportSyncResult> SyncAsync(
            CancellationToken cancellationToken,
            IProgress<string>? progress = null,
            bool latestOnly = false,
            DateTime? minimumReportDayExclusive = null)
        {
            LatestOnly = latestOnly;
            return Task.FromResult(_result);
        }
    }
}
