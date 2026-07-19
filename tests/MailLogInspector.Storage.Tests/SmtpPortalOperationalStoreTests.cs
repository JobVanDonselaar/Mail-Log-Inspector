using MailLogInspector.Core;
using MailLogInspector.Storage;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class SmtpPortalOperationalStoreTests
{
    [Fact]
    public void ConfigAndProbeHistory_RoundTrip()
    {
        string root = Path.Combine(Path.GetTempPath(), "mail-log-inspector-smtp-portal-" + Guid.NewGuid().ToString("N"));
        MailLogInspectorWorkspacePaths workspace = MailLogInspectorWorkspaceBootstrapper.Prepare(root);
        var store = new SmtpPortalOperationalStore(workspace.GmailOperationalDatabasePath);
        store.Initialize();
        var config = new SmtpPortalConfig(
            "portal-user@example.test",
            "encrypted-password",
            "encrypted-totp",
            "Proefdownload geslaagd",
            new DateTime(2026, 7, 18, 8, 0, 0, DateTimeKind.Utc));

        store.SaveConfig(config);
        store.UpsertProbeHistory(new SmtpPortalProbeHistoryRow(
            "NextGen_2026-07-17(00)_2026-07-18(00) (delivered + bounced + queue) (raw_event_stream)",
            new DateTime(2026, 7, 17),
            new DateTime(2026, 7, 18),
            "ABC123",
            @"C:\incoming\report.zip",
            1024,
            true,
            "ok",
            null,
            new DateTime(2026, 7, 18, 8, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(config, store.LoadConfig());
        SmtpPortalProbeHistoryRow row = Assert.Single(store.ReadProbeHistory(10));
        Assert.Equal("ABC123", row.SourceHash);
        Assert.True(row.AlreadyImported);
        Assert.Equal("ok", row.Status);
    }
}
