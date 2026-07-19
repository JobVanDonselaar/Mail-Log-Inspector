using MailLogInspector.App;
using MailLogInspector.Core;
using MailLogInspector.Storage;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class GmailReportSyncServiceTests
{
    [Fact]
    public void InferReportDay_UsesUtcCalendarDay()
    {
        DateTimeOffset receivedAt = new(2026, 7, 19, 0, 30, 0, TimeSpan.FromHours(-6));

        DateTime reportDay = GmailReportSyncService.InferReportDay(receivedAt);

        Assert.Equal(new DateTime(2026, 7, 18), reportDay);
    }

    [Fact]
    public async Task SyncAsync_ImportsAndDeletes_WhenZipMailIsEligible()
    {
        MailLogInspectorWorkspacePaths workspace = MailLogInspectorWorkspaceBootstrapper.Prepare(Path.Combine(Path.GetTempPath(), "gmail-sync-" + Guid.NewGuid().ToString("N")));
        var store = new GmailReportOperationalStore(workspace.GmailOperationalDatabasePath);
        store.Initialize();
        store.SaveConfig(new GmailReportConfig("reports@example.com", GmailAuthenticationMode.OAuth, "client-id", "client-secret", GmailOAuthService.ProtectRefreshToken("refresh-token"), null, false, 15, null, DateTime.UtcNow, DateTime.UtcNow, "Gekoppeld"));

        var mailClient = new FakeGmailMailClient(new GmailImapReportMessage(
            "msg-1",
            DateTimeOffset.UtcNow,
            "no-reply@smtp.com",
            "SMTP.com Report is Ready",
            "<a href=\"https://s0-reports-bucket.s3.ca-central-1.amazonaws.com/csv/test.zip\">Download Report</a>",
            null,
            "msg-uid-1"));
        var downloader = new FakeGmailZipDownloader();
        var importer = new FakeGmailZipImportRunner(success: true);
        var tokenProvider = new FakeGmailAccessTokenProvider();
        var service = new GmailReportSyncService(store, tokenProvider, mailClient, downloader, importer, workspace);

        GmailReportSyncResult result = await service.SyncAsync(CancellationToken.None);

        Assert.Equal(1, result.ImportedCount);
        Assert.Single(mailClient.DeletedMessageIds);
        Assert.True(store.HasSuccessfulMessage("msg-1", "https://s0-reports-bucket.s3.ca-central-1.amazonaws.com/csv/test.zip"));
        Assert.Equal(FakeGmailZipImportRunner.SourceHash, Assert.Single(store.ReadHistory(10)).SourceHash);
    }

    [Fact]
    public async Task SyncAsync_DoesNotDelete_WhenImportFails()
    {
        MailLogInspectorWorkspacePaths workspace = MailLogInspectorWorkspaceBootstrapper.Prepare(Path.Combine(Path.GetTempPath(), "gmail-sync-" + Guid.NewGuid().ToString("N")));
        var store = new GmailReportOperationalStore(workspace.GmailOperationalDatabasePath);
        store.Initialize();
        store.SaveConfig(new GmailReportConfig("reports@example.com", GmailAuthenticationMode.OAuth, "client-id", "client-secret", GmailOAuthService.ProtectRefreshToken("refresh-token"), null, false, 15, null, DateTime.UtcNow, DateTime.UtcNow, "Gekoppeld"));

        var mailClient = new FakeGmailMailClient(new GmailImapReportMessage(
            "msg-2",
            DateTimeOffset.UtcNow,
            "no-reply@smtp.com",
            "SMTP.com Report is Ready",
            "<a href=\"https://s0-reports-bucket.s3.ca-central-1.amazonaws.com/csv/test-2.zip\">Download Report</a>",
            null,
            "msg-uid-2"));
        var downloader = new FakeGmailZipDownloader();
        var importer = new FakeGmailZipImportRunner(success: false);
        var tokenProvider = new FakeGmailAccessTokenProvider();
        var service = new GmailReportSyncService(store, tokenProvider, mailClient, downloader, importer, workspace);

        GmailReportSyncResult result = await service.SyncAsync(CancellationToken.None);

        Assert.Equal(1, result.FailedCount);
        Assert.Empty(mailClient.DeletedMessageIds);
    }

    [Fact]
    public async Task SyncAsync_ContinuesAfterDownloadFailure_AndStillImportsLaterMessage()
    {
        MailLogInspectorWorkspacePaths workspace = MailLogInspectorWorkspaceBootstrapper.Prepare(Path.Combine(Path.GetTempPath(), "gmail-sync-" + Guid.NewGuid().ToString("N")));
        var store = new GmailReportOperationalStore(workspace.GmailOperationalDatabasePath);
        store.Initialize();
        store.SaveConfig(new GmailReportConfig("reports@example.com", GmailAuthenticationMode.OAuth, "client-id", "client-secret", GmailOAuthService.ProtectRefreshToken("refresh-token"), null, false, 15, null, DateTime.UtcNow, DateTime.UtcNow, "Gekoppeld"));

        var failingMessage = new GmailImapReportMessage(
            "msg-fail",
            DateTimeOffset.UtcNow,
            "no-reply@smtp.com",
            "SMTP.com Report is Ready",
            "<a href=\"https://s0-reports-bucket.s3.ca-central-1.amazonaws.com/csv/fail.zip\">Download Report</a>",
            null,
            "msg-uid-fail");
        var successMessage = new GmailImapReportMessage(
            "msg-ok",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            "no-reply@smtp.com",
            "SMTP.com Report is Ready",
            "<a href=\"https://s0-reports-bucket.s3.ca-central-1.amazonaws.com/csv/ok.zip\">Download Report</a>",
            null,
            "msg-uid-ok");

        var mailClient = new FakeGmailMailClient(failingMessage, successMessage);
        var downloader = new FakeGmailZipDownloader("fail.zip");
        var importer = new FakeGmailZipImportRunner(success: true);
        var tokenProvider = new FakeGmailAccessTokenProvider();
        var service = new GmailReportSyncService(store, tokenProvider, mailClient, downloader, importer, workspace);

        GmailReportSyncResult result = await service.SyncAsync(CancellationToken.None);

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Single(mailClient.DeletedMessageIds);
        Assert.Equal("msg-uid-ok", mailClient.DeletedMessageIds[0]);
        Assert.True(store.HasSuccessfulMessage("msg-ok", "https://s0-reports-bucket.s3.ca-central-1.amazonaws.com/csv/ok.zip"));
    }

    [Fact]
    public async Task SyncAsync_UsesAppPasswordMode_WithoutRequestingOAuthToken()
    {
        MailLogInspectorWorkspacePaths workspace = MailLogInspectorWorkspaceBootstrapper.Prepare(Path.Combine(Path.GetTempPath(), "gmail-sync-" + Guid.NewGuid().ToString("N")));
        var store = new GmailReportOperationalStore(workspace.GmailOperationalDatabasePath);
        store.Initialize();
        store.SaveConfig(new GmailReportConfig(
            "reports@example.com",
            GmailAuthenticationMode.AppPassword,
            null,
            null,
            null,
            GmailOAuthService.ProtectSecret("app-password-1234"),
            false,
            15,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow,
            "Gekoppeld"));

        var mailClient = new FakeGmailMailClient(new GmailImapReportMessage(
            "msg-app-password",
            DateTimeOffset.UtcNow,
            "no-reply@smtp.com",
            "SMTP.com Report is Ready",
            "<a href=\"https://s0-reports-bucket.s3.ca-central-1.amazonaws.com/csv/app-password.zip\">Download Report</a>",
            null,
            "msg-uid-app"));
        var downloader = new FakeGmailZipDownloader();
        var importer = new FakeGmailZipImportRunner(success: true);
        var tokenProvider = new FakeGmailAccessTokenProvider();
        var service = new GmailReportSyncService(store, tokenProvider, mailClient, downloader, importer, workspace);

        GmailReportSyncResult result = await service.SyncAsync(CancellationToken.None);

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(0, tokenProvider.CallCount);
        Assert.Equal(GmailAuthenticationMode.AppPassword, mailClient.LastSettings?.AuthenticationMode);
        Assert.Equal("app-password-1234", mailClient.LastSettings?.AppPassword);
    }

    [Fact]
    public async Task SyncAsync_KeepsDownloadAndImportSuccessful_WhenOnlyDeletionFails()
    {
        MailLogInspectorWorkspacePaths workspace = MailLogInspectorWorkspaceBootstrapper.Prepare(Path.Combine(Path.GetTempPath(), "gmail-sync-" + Guid.NewGuid().ToString("N")));
        var store = new GmailReportOperationalStore(workspace.GmailOperationalDatabasePath);
        store.Initialize();
        store.SaveConfig(new GmailReportConfig(
            "reports@example.com",
            GmailAuthenticationMode.AppPassword,
            null,
            null,
            null,
            GmailOAuthService.ProtectSecret("app-password-1234"),
            false,
            15,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow,
            "Gekoppeld"));

        var mailClient = new FakeGmailMailClient(
            deleteException: new InvalidOperationException("The folder is not currently open in read-write mode."),
            new GmailImapReportMessage(
                "msg-archive-fail",
                DateTimeOffset.UtcNow,
                "no-reply@smtp.com",
                "SMTP.com Report is Ready",
                "<a href=\"https://s0-reports-bucket.s3.ca-central-1.amazonaws.com/csv/archive-fail.zip\">Download Report</a>",
                null,
                "msg-uid-archive-fail"));
        var downloader = new FakeGmailZipDownloader();
        var importer = new FakeGmailZipImportRunner(success: true);
        var tokenProvider = new FakeGmailAccessTokenProvider();
        var service = new GmailReportSyncService(store, tokenProvider, mailClient, downloader, importer, workspace);

        GmailReportSyncResult result = await service.SyncAsync(CancellationToken.None);
        GmailReportHistoryRow history = Assert.Single(store.ReadHistory(10));

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(0, result.DeletedCount);
        Assert.Equal("ok", history.DownloadStatus);
        Assert.Equal("ok", history.ImportStatus);
        Assert.False(history.Archived);
        Assert.Contains("verwijderen van Gmail-mail mislukte", history.ErrorText);
    }

    [Fact]
    public async Task SyncAsync_RetriesOnlyDeletion_WhenPreviousImportSucceeded()
    {
        MailLogInspectorWorkspacePaths workspace = MailLogInspectorWorkspaceBootstrapper.Prepare(Path.Combine(Path.GetTempPath(), "gmail-sync-" + Guid.NewGuid().ToString("N")));
        var store = new GmailReportOperationalStore(workspace.GmailOperationalDatabasePath);
        store.Initialize();
        store.SaveConfig(new GmailReportConfig("reports@example.com", GmailAuthenticationMode.AppPassword, null, null, null, GmailOAuthService.ProtectSecret("app-password-1234"), false, 15, null, DateTime.UtcNow, DateTime.UtcNow, "Gekoppeld"));
        var message = new GmailImapReportMessage("msg-delete-retry", DateTimeOffset.UtcNow, "no-reply@smtp.com", "SMTP.com Report is Ready", "<a href=\"https://s0-reports-bucket.s3.ca-central-1.amazonaws.com/csv/delete-retry.zip\">Download Report</a>", null, "msg-uid-delete-retry");
        var importer = new FakeGmailZipImportRunner(success: true);
        var failingService = new GmailReportSyncService(store, new FakeGmailAccessTokenProvider(), new FakeGmailMailClient(new InvalidOperationException("delete failed"), message), new FakeGmailZipDownloader(), importer, workspace);
        await failingService.SyncAsync(CancellationToken.None);

        var retryClient = new FakeGmailMailClient(message);
        var retryService = new GmailReportSyncService(store, new FakeGmailAccessTokenProvider(), retryClient, new FakeGmailZipDownloader(), importer, workspace);
        GmailReportSyncResult retryResult = await retryService.SyncAsync(CancellationToken.None);

        Assert.Equal(1, importer.CallCount);
        Assert.Equal(1, retryResult.DeletedCount);
        Assert.Equal(0, retryResult.ImportedCount);
        Assert.Single(retryClient.DeletedMessageIds);
        Assert.True(Assert.Single(store.ReadHistory(10)).Archived);
    }

    [Fact]
    public async Task SyncAsync_DeletesDuplicateZipMessage_WithoutImportingTwice()
    {
        MailLogInspectorWorkspacePaths workspace = MailLogInspectorWorkspaceBootstrapper.Prepare(Path.Combine(Path.GetTempPath(), "gmail-sync-" + Guid.NewGuid().ToString("N")));
        var store = new GmailReportOperationalStore(workspace.GmailOperationalDatabasePath);
        store.Initialize();
        store.SaveConfig(new GmailReportConfig("reports@example.com", GmailAuthenticationMode.AppPassword, null, null, null, GmailOAuthService.ProtectSecret("app-password-1234"), false, 15, null, DateTime.UtcNow, DateTime.UtcNow, "Gekoppeld"));
        const string body = "<a href=\"https://s0-reports-bucket.s3.ca-central-1.amazonaws.com/csv/shared.zip\">Download Report</a>";
        var newest = new GmailImapReportMessage("msg-new", DateTimeOffset.UtcNow, "no-reply@smtp.com", "SMTP.com Report is Ready", body, null, "uid-new");
        var older = new GmailImapReportMessage("msg-old", DateTimeOffset.UtcNow.AddMinutes(-1), "no-reply@smtp.com", "SMTP.com Report is Ready", body, null, "uid-old");
        var importer = new FakeGmailZipImportRunner(success: true);
        var mailClient = new FakeGmailMailClient(newest, older);
        var service = new GmailReportSyncService(store, new FakeGmailAccessTokenProvider(), mailClient, new FakeGmailZipDownloader(), importer, workspace);
        GmailReportSyncResult result = await service.SyncAsync(CancellationToken.None);

        Assert.Equal(1, importer.CallCount);
        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(2, result.DeletedCount);
        Assert.Equal(2, mailClient.DeletedMessageIds.Count);
        Assert.Equal(2, store.ReadHistory(10).Count);
    }
    [Fact]
    public async Task SyncAsync_LatestOnlyImportsOnlyNewestValidMessage()
    {
        MailLogInspectorWorkspacePaths workspace = MailLogInspectorWorkspaceBootstrapper.Prepare(
            Path.Combine(Path.GetTempPath(), "gmail-sync-latest-" + Guid.NewGuid().ToString("N")));
        var store = new GmailReportOperationalStore(workspace.GmailOperationalDatabasePath);
        store.Initialize();
        store.SaveConfig(new GmailReportConfig(
            "reports@example.com",
            GmailAuthenticationMode.AppPassword,
            null,
            null,
            null,
            GmailOAuthService.ProtectSecret("app-password-1234"),
            false,
            15,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow,
            "Gekoppeld"));
        var newest = new GmailImapReportMessage(
            "msg-newest",
            new DateTimeOffset(2026, 7, 19, 0, 10, 0, TimeSpan.Zero),
            "no-reply@smtp.com",
            "SMTP.com Report is Ready",
            "<a href=\"https://example.test/newest.zip\">Download Report</a>",
            null,
            "uid-newest");
        var older = new GmailImapReportMessage(
            "msg-older",
            new DateTimeOffset(2026, 7, 18, 0, 10, 0, TimeSpan.Zero),
            "no-reply@smtp.com",
            "SMTP.com Report is Ready",
            "<a href=\"https://example.test/older.zip\">Download Report</a>",
            null,
            "uid-older");
        var importer = new FakeGmailZipImportRunner(success: true);
        var service = new GmailReportSyncService(
            store,
            new FakeGmailAccessTokenProvider(),
            new FakeGmailMailClient(older, newest),
            new FakeGmailZipDownloader(),
            importer,
            workspace);

        GmailReportSyncResult result = await service.SyncAsync(
            CancellationToken.None,
            latestOnly: true);

        Assert.Equal(1, importer.CallCount);
        Assert.Equal(1, result.ImportedCount);
    }
    [Fact]
    public async Task SyncAsync_LatestOnlyDoesNotTrustStaleOperationalImportHistory()
    {
        MailLogInspectorWorkspacePaths workspace = MailLogInspectorWorkspaceBootstrapper.Prepare(
            Path.Combine(Path.GetTempPath(), "gmail-sync-empty-db-" + Guid.NewGuid().ToString("N")));
        var store = new GmailReportOperationalStore(workspace.GmailOperationalDatabasePath);
        store.Initialize();
        store.SaveConfig(new GmailReportConfig(
            "reports@example.com",
            GmailAuthenticationMode.AppPassword,
            null,
            null,
            null,
            GmailOAuthService.ProtectSecret("app-password-1234"),
            false,
            15,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow,
            "Gekoppeld"));
        DateTimeOffset receivedAt = new(2026, 7, 19, 0, 10, 0, TimeSpan.Zero);
        const string zipUrl = "https://example.test/latest.zip";
        store.UpsertHistory(new GmailReportHistoryRow(
            "msg-latest",
            receivedAt,
            "no-reply@smtp.com",
            "SMTP.com Report is Ready",
            zipUrl,
            "ok",
            "ok",
            false,
            null,
            receivedAt.UtcDateTime,
            receivedAt.UtcDateTime,
            "OLD-HASH"));
        var message = new GmailImapReportMessage(
            "msg-latest",
            receivedAt,
            "no-reply@smtp.com",
            "SMTP.com Report is Ready",
            $"<a href=\"{zipUrl}\">Download Report</a>",
            null,
            "uid-latest");
        var importer = new FakeGmailZipImportRunner(success: true);
        var service = new GmailReportSyncService(
            store,
            new FakeGmailAccessTokenProvider(),
            new FakeGmailMailClient(message),
            new FakeGmailZipDownloader(),
            importer,
            workspace);

        GmailReportSyncResult result = await service.SyncAsync(
            CancellationToken.None,
            latestOnly: true);

        Assert.Equal(1, importer.CallCount);
        Assert.Equal(1, result.ImportedCount);
    }

    private sealed class FakeGmailAccessTokenProvider : IGmailAccessTokenProvider
    {
        public int CallCount { get; private set; }

        public Task<string> GetAccessTokenAsync(GmailOAuthConfig config, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult("access-token");
        }
    }

    private sealed class FakeGmailMailClient : IGmailImapReportClient
    {
        private readonly IReadOnlyList<GmailImapReportMessage> _messages;
        private readonly Exception? _deleteException;

        public List<string> DeletedMessageIds { get; } = new();
        public GmailImapConnectionSettings? LastSettings { get; private set; }

        public FakeGmailMailClient(Exception? deleteException = null, params GmailImapReportMessage[] messages)
        {
            _deleteException = deleteException;
            _messages = messages;
        }

        public FakeGmailMailClient(params GmailImapReportMessage[] messages)
            : this(null, messages)
        {
        }

        public Task<IReadOnlyList<GmailImapReportMessage>> FetchInboxCandidatesAsync(GmailImapConnectionSettings settings, CancellationToken cancellationToken)
        {
            LastSettings = settings;
            return Task.FromResult(_messages);
        }

        public Task<IReadOnlyList<GmailImapReportMessage>> FetchCatchupCandidatesAsync(GmailImapConnectionSettings settings, DateTime sinceUtc, CancellationToken cancellationToken)
        {
            LastSettings = settings;
            return Task.FromResult<IReadOnlyList<GmailImapReportMessage>>(Array.Empty<GmailImapReportMessage>());
        }

        public Task DeleteMessagePermanentlyAsync(GmailImapConnectionSettings settings, GmailImapReportMessage message, CancellationToken cancellationToken)
        {
            LastSettings = settings;
            if (_deleteException != null)
            {
                throw _deleteException;
            }

            DeletedMessageIds.Add(message.MessageUniqueId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeGmailZipDownloader : IGmailZipDownloader
    {
        private readonly string? _failingFileName;

        public FakeGmailZipDownloader(string? failingFileName = null)
        {
            _failingFileName = failingFileName;
        }

        public Task<string> DownloadAsync(string zipUrl, string targetDirectory, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(_failingFileName) && string.Equals(Path.GetFileName(new Uri(zipUrl).AbsolutePath), _failingFileName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Download mislukt.");
            }

            Directory.CreateDirectory(targetDirectory);
            string path = Path.Combine(targetDirectory, Path.GetFileName(new Uri(zipUrl).AbsolutePath));
            File.WriteAllBytes(path, new byte[] { 0x50, 0x4B, 0x03, 0x04 });
            return Task.FromResult(path);
        }
    }

    private sealed class FakeGmailZipImportRunner : IGmailZipImportRunner
    {
        public const string SourceHash = "FAKE-SOURCE-HASH";

        private readonly bool _success;

        public FakeGmailZipImportRunner(bool success)
        {
            _success = success;
        }

        public int CallCount { get; private set; }

        public Task<GmailZipImportOutcome> ImportAsync(string zipPath, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new GmailZipImportOutcome(_success, SourceHash));
        }
    }
}
