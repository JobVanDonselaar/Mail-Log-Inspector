using MailLogInspector.Core;
using MailLogInspector.Storage;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class GmailReportOperationalStoreTests
{
    [Fact]
    public void SaveConfig_AndLoadConfig_RoundTrip()
    {
        string rootPath = Path.Combine(Path.GetTempPath(), "mail-log-inspector-gmail-config-" + Guid.NewGuid().ToString("N"));
        MailLogInspectorWorkspacePaths workspace = MailLogInspectorWorkspaceBootstrapper.Prepare(rootPath);
        var store = new GmailReportOperationalStore(workspace.GmailOperationalDatabasePath);
        store.Initialize();

        var config = new GmailReportConfig(
            "reports@example.com",
            GmailAuthenticationMode.AppPassword,
            "client-id",
            "client-secret",
            "encrypted-refresh-token",
            "encrypted-app-password",
            true,
            15,
            DateTime.UtcNow,
            DateTime.UtcNow,
            DateTime.UtcNow,
            "Gekoppeld",
            true);

        store.SaveConfig(config);
        GmailReportConfig loaded = store.LoadConfig();

        Assert.Equal(config.AccountEmailAddress, loaded.AccountEmailAddress);
        Assert.Equal(config.AuthenticationMode, loaded.AuthenticationMode);
        Assert.Equal(config.ClientId, loaded.ClientId);
        Assert.Equal(config.ClientSecret, loaded.ClientSecret);
        Assert.Equal(config.EncryptedRefreshToken, loaded.EncryptedRefreshToken);
        Assert.Equal(config.EncryptedAppPassword, loaded.EncryptedAppPassword);
        Assert.Equal(config.ConnectionStatus, loaded.ConnectionStatus);
    }
    [Fact]
    public void SaveConfig_AndLoadConfig_RoundTripCustomImapConnection()
    {
        string rootPath = Path.Combine(Path.GetTempPath(), "mail-log-inspector-imap-profile-" + Guid.NewGuid().ToString("N"));
        MailLogInspectorWorkspacePaths workspace = MailLogInspectorWorkspaceBootstrapper.Prepare(rootPath);
        var store = new GmailReportOperationalStore(workspace.GmailOperationalDatabasePath);
        store.Initialize();

        GmailReportConfig config = GmailReportConfig.Empty with
        {
            AccountEmailAddress = "rapporten@example.test",
            AuthenticationMode = GmailAuthenticationMode.AppPassword,
            EncryptedAppPassword = "dpapi:secret",
            ImapProvider = ImapProvider.Custom,
            ImapHost = "mail.example.test",
            ImapPort = 1993,
            ImapUseSsl = false
        };

        store.SaveConfig(config);
        GmailReportConfig loaded = store.LoadConfig();

        Assert.Equal(ImapProvider.Custom, loaded.ImapProvider);
        Assert.Equal("mail.example.test", loaded.ImapHost);
        Assert.Equal(1993, loaded.ImapPort);
        Assert.False(loaded.ImapUseSsl);
    }
    [Fact]
    public void HasSuccessfulMessage_ReturnsTrue_WhenMessageOrZipWasAlreadyProcessed()
    {
        string rootPath = Path.Combine(Path.GetTempPath(), "mail-log-inspector-gmail-history-" + Guid.NewGuid().ToString("N"));
        MailLogInspectorWorkspacePaths workspace = MailLogInspectorWorkspaceBootstrapper.Prepare(rootPath);
        var store = new GmailReportOperationalStore(workspace.GmailOperationalDatabasePath);
        store.Initialize();

        store.UpsertHistory(new GmailReportHistoryRow(
            "message-1",
            DateTimeOffset.UtcNow,
            "no-reply@smtp.com",
            "SMTP.com Report is Ready",
            "https://example.test/report.zip",
            "ok",
            "ok",
            true,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow));

        Assert.True(store.HasSuccessfulMessage("message-1", "https://other.example/test.zip"));
        Assert.True(store.HasSuccessfulMessage("message-2", "https://example.test/report.zip"));
    }

    [Fact]
    public void ReadLatestSuccessfulImportAtUtc_IgnoresNewerFailedAttempts()
    {
        string rootPath = Path.Combine(Path.GetTempPath(), "mail-log-inspector-gmail-latest-success-" + Guid.NewGuid().ToString("N"));
        MailLogInspectorWorkspacePaths workspace = MailLogInspectorWorkspaceBootstrapper.Prepare(rootPath);
        var store = new GmailReportOperationalStore(workspace.GmailOperationalDatabasePath);
        store.Initialize();
        DateTime expected = new(2026, 7, 12, 8, 30, 0, DateTimeKind.Utc);

        store.UpsertHistory(CreateHistoryRow("success", "ok", expected));
        store.UpsertHistory(CreateHistoryRow("failure", "failed", expected.AddHours(5)));

        Assert.Equal(expected, store.ReadLatestSuccessfulImportAtUtc());
    }

    [Fact]
    public void History_RoundTripsSourceHash_AndBackfillsOnlyUniqueFileNames()
    {
        string rootPath = Path.Combine(Path.GetTempPath(), "mail-log-inspector-gmail-source-hash-" + Guid.NewGuid().ToString("N"));
        MailLogInspectorWorkspacePaths workspace = MailLogInspectorWorkspaceBootstrapper.Prepare(rootPath);
        var store = new GmailReportOperationalStore(workspace.GmailOperationalDatabasePath);
        store.Initialize();
        DateTime importedAt = new(2026, 7, 18, 0, 15, 0, DateTimeKind.Utc);

        store.UpsertHistory(CreateHistoryRow("linked", "ok", importedAt) with
        {
            ZipUrl = "https://example.test/linked.zip",
            SourceHash = "HASH-LINKED"
        });
        store.UpsertHistory(CreateHistoryRow("unique", "ok", importedAt) with
        {
            ZipUrl = "https://example.test/unique.zip"
        });
        store.UpsertHistory(CreateHistoryRow("ambiguous", "ok", importedAt) with
        {
            ZipUrl = "https://example.test/shared.zip"
        });

        MailLogInspectorImportedFile[] imports =
        [
            CreateImport(1, "unique.zip", "HASH-UNIQUE"),
            CreateImport(2, "shared.zip", "HASH-SHARED-A"),
            CreateImport(3, "shared.zip", "HASH-SHARED-B")
        ];

        store.BackfillMissingSourceHashes(imports);
        IReadOnlyList<GmailReportHistoryRow> history = store.ReadHistory(10);

        Assert.Equal("HASH-LINKED", history.Single(row => row.GmailMessageId == "linked").SourceHash);
        Assert.Equal("HASH-UNIQUE", history.Single(row => row.GmailMessageId == "unique").SourceHash);
        Assert.Null(history.Single(row => row.GmailMessageId == "ambiguous").SourceHash);
    }

    [Fact]
    public void UpsertHistory_PreservesKnownSourceHash_WhenRetryHasNoHash()
    {
        string rootPath = Path.Combine(Path.GetTempPath(), "mail-log-inspector-gmail-preserve-hash-" + Guid.NewGuid().ToString("N"));
        MailLogInspectorWorkspacePaths workspace = MailLogInspectorWorkspaceBootstrapper.Prepare(rootPath);
        var store = new GmailReportOperationalStore(workspace.GmailOperationalDatabasePath);
        store.Initialize();
        DateTime attemptedAt = new(2026, 7, 18, 0, 15, 0, DateTimeKind.Utc);

        GmailReportHistoryRow imported = CreateHistoryRow("retry", "ok", attemptedAt) with { SourceHash = "HASH-RETRY" };
        store.UpsertHistory(imported);
        store.UpsertHistory(imported with { Archived = true, SourceHash = null, LastAttemptAtUtc = attemptedAt.AddMinutes(5) });

        Assert.Equal("HASH-RETRY", store.ReadHistory(10).Single().SourceHash);
    }
    private static MailLogInspectorImportedFile CreateImport(long importId, string fileName, string sourceHash)
    {
        return new MailLogInspectorImportedFile(
            importId,
            Path.Combine("C:\\imports", fileName),
            fileName,
            sourceHash,
            DateTime.UtcNow,
            null,
            null,
            10,
            null);
    }
    private static GmailReportHistoryRow CreateHistoryRow(string messageId, string importStatus, DateTime attemptedAtUtc)
    {
        return new GmailReportHistoryRow(
            messageId,
            new DateTimeOffset(attemptedAtUtc),
            "no-reply@smtp.com",
            "SMTP.com Report is Ready",
            $"https://example.test/{messageId}.zip",
            "ok",
            importStatus,
            false,
            null,
            attemptedAtUtc,
            attemptedAtUtc);
    }
}
