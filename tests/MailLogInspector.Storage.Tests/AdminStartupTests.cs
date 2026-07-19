using MailLogInspector.App;
using MailLogInspector.Core;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class AdminStartupTests
{
    [Theory]
    [InlineData("/admin")]
    [InlineData("/ADMIN")]
    public void StartupRequestParser_RecognizesAdmin(string argument)
    {
        Assert.Equal(AppActivationRequest.Admin, AppStartupRequestParser.Parse([argument]));
    }

    [Fact]
    public void StartupRequestParser_DefaultsToActivate()
    {
        Assert.Equal(AppActivationRequest.Activate, AppStartupRequestParser.Parse([]));
    }

    [Fact]
    public void ConfigBuilder_PreservesStoredSecretsWhenFieldsStayEmpty()
    {
        GmailReportConfig stored = new(
            "old@example.test",
            GmailAuthenticationMode.AppPassword,
            "old-client-id",
            "dpapi:old-client-secret",
            "dpapi:refresh-token",
            "dpapi:old-app-password",
            false,
            60,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow,
            "Gekoppeld",
            false);
        GmailAdminSettingsInput input = new(
            "reports@example.test",
            GmailAuthenticationMode.AppPassword,
            "client-id",
            string.Empty,
            string.Empty);

        GmailReportConfig result = GmailAdminConfigBuilder.Build(stored, input);

        Assert.Equal("dpapi:old-client-secret", result.ClientSecret);
        Assert.Equal("dpapi:old-app-password", result.EncryptedAppPassword);
    }

    [Fact]
    public void LegacyOAuthClientSecret_IsMigratedToDpapi()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "mail-log-client-secret-" + Guid.NewGuid().ToString("N"));
        var store = new GmailReportOperationalStore(Path.Combine(root, "operational.sqlite"));
        store.Initialize();
        store.SaveConfig(GmailReportConfig.Empty with
        {
            ClientSecret = "legacy-plaintext-secret"
        });

        Assert.True(GmailOAuthService.MigrateLegacyClientSecret(store));

        string protectedSecret = Assert.IsType<string>(store.LoadConfig().ClientSecret);
        Assert.StartsWith("dpapi:", protectedSecret, StringComparison.Ordinal);
        Assert.Equal(
            "legacy-plaintext-secret",
            GmailOAuthService.UnprotectClientSecret(protectedSecret));
        Assert.False(GmailOAuthService.MigrateLegacyClientSecret(store));
    }


    [Fact]
    public void ImapConfigBuilder_UsesKnownProviderDefaultsAndKeepsStoredPassword()
    {
        GmailReportConfig stored = GmailReportConfig.Empty with
        {
            EncryptedAppPassword = "dpapi:stored-password"
        };

        GmailReportConfig result = GmailAdminConfigBuilder.Build(
            stored,
            new GmailAdminSettingsInput(
                "rapporten@example.test",
                GmailAuthenticationMode.AppPassword,
                string.Empty,
                string.Empty,
                string.Empty,
                ImapProvider.Microsoft365,
                string.Empty,
                null,
                true));

        Assert.Equal(ImapProvider.Microsoft365, result.ImapProvider);
        Assert.Equal("outlook.office365.com", result.ImapHost);
        Assert.Equal(993, result.ImapPort);
        Assert.True(result.ImapUseSsl);
        Assert.Equal("dpapi:stored-password", result.EncryptedAppPassword);
    }

    [Fact]
    public void ImapConfigBuilder_UsesCustomServerValues()
    {
        GmailReportConfig result = GmailAdminConfigBuilder.Build(
            GmailReportConfig.Empty,
            new GmailAdminSettingsInput(
                "rapporten@example.test",
                GmailAuthenticationMode.AppPassword,
                string.Empty,
                string.Empty,
                "secret",
                ImapProvider.Custom,
                "mail.example.test",
                143,
                false));

        Assert.Equal(ImapProvider.Custom, result.ImapProvider);
        Assert.Equal("mail.example.test", result.ImapHost);
        Assert.Equal(143, result.ImapPort);
        Assert.False(result.ImapUseSsl);
        Assert.False(string.IsNullOrWhiteSpace(result.EncryptedAppPassword));
    }
    [Fact]
    public void SyncConfigBuilder_PersistsGeneralSettingsWithoutGmail()
    {
        DateTime attempt = new(2026, 7, 19, 1, 0, 0, DateTimeKind.Utc);
        DateTime success = attempt.AddMinutes(1);
        ReportSyncConfig stored = new(ReportSyncMode.GmailOnly, attempt, success);
        var input = new AdminSyncSettingsInput(
            ReportSyncMode.DirectOnly,
            AutoSyncEnabled: true,
            CloseToTrayEnabled: true);

        ReportSyncConfig result = AdminSyncConfigBuilder.Build(stored, input);

        Assert.Equal(ReportSyncMode.DirectOnly, result.Mode);
        Assert.True(result.AutoSyncEnabled);
        Assert.True(result.CloseToTrayEnabled);
    }

    [Fact]
    public void SmtpPortalConfigBuilder_PreservesStoredSecretsWhenFieldsStayEmpty()
    {
        SmtpPortalConfig stored = new(
            "old@example.test",
            "encrypted-password",
            "encrypted-totp",
            "Getest",
            DateTime.UtcNow);

        SmtpPortalConfig result = SmtpPortalAdminConfigBuilder.Build(
            stored,
            new SmtpPortalAdminSettingsInput("new@example.test", string.Empty, string.Empty));

        Assert.Equal("new@example.test", result.Username);
        Assert.Equal("encrypted-password", result.EncryptedPassword);
        Assert.Equal("encrypted-totp", result.EncryptedTotpSecret);
    }
    [Fact]
    public void SmtpPortalConfigBuilder_PreservesStoredSecretsWhenPlaceholdersRemainVisible()
    {
        SmtpPortalConfig stored = new(
            "old@example.test",
            "encrypted-password",
            "encrypted-totp",
            "Getest",
            DateTime.UtcNow);

        SmtpPortalConfig result = SmtpPortalAdminConfigBuilder.Build(
            stored,
            new SmtpPortalAdminSettingsInput(
                "old@example.test",
                SmtpPortalAdminConfigBuilder.StoredSecretPlaceholder,
                SmtpPortalAdminConfigBuilder.StoredSecretPlaceholder));

        Assert.Equal("encrypted-password", result.EncryptedPassword);
        Assert.Equal("encrypted-totp", result.EncryptedTotpSecret);
    }

    [Fact]
    public void AdminWindow_ContainsRequiredControls()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string xamlPath = Path.Combine(root, "src", "MailLogInspector.App", "AdminSettingsWindow.xaml");

        Assert.True(File.Exists(xamlPath));
        string xaml = File.ReadAllText(xamlPath);
        Assert.Contains("Name=\"AdminSyncSourceComboBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"RunReportSyncNowButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"CancelReportSyncButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"CancelReportSyncButton_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Nu synchroniseren\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Direct downloaden, bij fout IMAP", xaml, StringComparison.Ordinal);
        Assert.Contains("Alleen IMAP", xaml, StringComparison.Ordinal);
        Assert.Contains("Alleen direct downloaden", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"AdminGmailAuthModeComboBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"AdminGmailAccountTextBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"AdminGmailAppPasswordBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"AdminGmailAppPasswordSavedTextBlock\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"AdminImapProviderComboBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"AdminCustomImapHostTextBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"AdminCustomImapPortTextBox\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScrollViewer", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"AdminAutoSyncCheckBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"AdminCloseToTrayCheckBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"TestAdminConnectionButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"SaveAdminSettingsButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"CancelAdminSettingsButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"SMTP.com direct downloaden\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"AdminSmtpPortalUsernameTextBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"AdminSmtpPortalPasswordBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"AdminSmtpPortalPasswordSavedTextBlock\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"AdminSmtpPortalTotpSecretBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"AdminSmtpPortalTotpSavedTextBlock\"", xaml, StringComparison.Ordinal);
        Assert.Contains("GotKeyboardFocus=\"AdminSmtpPortalSecretBox_GotKeyboardFocus\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"RunSmtpPortalProbeButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"RunVisibleSmtpPortalProbeButton\"", xaml, StringComparison.Ordinal);
        Assert.True(
            xaml.IndexOf("Name=\"AdminAutoSyncCheckBox\"", StringComparison.Ordinal) <
            xaml.IndexOf("Text=\"SMTP.com direct downloaden\"", StringComparison.Ordinal));
        Assert.True(
            xaml.IndexOf("Text=\"SMTP.com direct downloaden\"", StringComparison.Ordinal) <
            xaml.IndexOf("Text=\"IMAP-rapportkoppeling\"", StringComparison.Ordinal));
    }

    [Fact]
    public void App_ShowsAdminBeforeConstructingFirstMainWindow()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string app = File.ReadAllText(Path.Combine(root, "src", "MailLogInspector.App", "App.cs"));

        Assert.Contains("ShowAdminSettings", app, StringComparison.Ordinal);
        Assert.Contains("AppActivationRequest.Admin", app, StringComparison.Ordinal);
        Assert.True(
            app.IndexOf("ShowAdminSettings", StringComparison.Ordinal) <
            app.LastIndexOf("new MainWindow()", StringComparison.Ordinal));
    }
}
