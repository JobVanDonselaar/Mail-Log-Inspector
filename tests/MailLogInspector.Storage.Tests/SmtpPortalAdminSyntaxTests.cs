using MailLogInspector.App;
using MailLogInspector.Storage;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class SmtpPortalAdminSyntaxTests
{
    [Fact]
    public void ConfigBuilder_StoresValidatedCustomSyntaxAndPreservesRuntimeFields()
    {
        DateTime lastProbe = new(2026, 7, 19, 8, 0, 0, DateTimeKind.Utc);
        DateTime lastUse = new(2026, 7, 19, 8, 30, 0, DateTimeKind.Utc);
        var stored = new SmtpPortalConfig(
            "old@example.test",
            "encrypted-password",
            "encrypted-totp",
            "Getest",
            lastProbe,
            LastSuccessfulPortalUseAtUtc: lastUse);

        SmtpPortalConfig result = SmtpPortalAdminConfigBuilder.Build(
            stored,
            new SmtpPortalAdminSettingsInput(
                "new@example.test",
                string.Empty,
                string.Empty,
                UseDefaultReportSyntax: false,
                CustomReportSyntax: "  Exquise_{start}_{end}_dagrapport  "));

        Assert.Equal("new@example.test", result.Username);
        Assert.False(result.UseDefaultReportSyntax);
        Assert.Equal("Exquise_{start}_{end}_dagrapport", result.CustomReportSyntax);
        Assert.Equal("encrypted-password", result.EncryptedPassword);
        Assert.Equal("encrypted-totp", result.EncryptedTotpSecret);
        Assert.Equal("Getest", result.ConnectionStatus);
        Assert.Equal(lastProbe, result.LastProbeAtUtc);
        Assert.Equal(lastUse, result.LastSuccessfulPortalUseAtUtc);
    }

    [Fact]
    public void ConfigBuilder_DefaultModeKeepsStoredCustomSyntaxForLaterReuse()
    {
        var stored = SmtpPortalConfig.Empty with
        {
            UseDefaultReportSyntax = false,
            CustomReportSyntax = "Exquise_{start}_{end}"
        };

        SmtpPortalConfig result = SmtpPortalAdminConfigBuilder.Build(
            stored,
            new SmtpPortalAdminSettingsInput(
                "user@example.test",
                string.Empty,
                string.Empty,
                UseDefaultReportSyntax: true,
                CustomReportSyntax: string.Empty));

        Assert.True(result.UseDefaultReportSyntax);
        Assert.Equal("Exquise_{start}_{end}", result.CustomReportSyntax);
    }

    [Fact]
    public void ConfigBuilder_RejectsInvalidCustomSyntax()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            SmtpPortalAdminConfigBuilder.Build(
                SmtpPortalConfig.Empty,
                new SmtpPortalAdminSettingsInput(
                    "user@example.test",
                    string.Empty,
                    string.Empty,
                    UseDefaultReportSyntax: false,
                    CustomReportSyntax: "Exquise_{start}")));

        Assert.Equal("Gebruik {end} exact één keer.", exception.Message);
    }

    [Fact]
    public void AdminWindow_ContainsCompactSyntaxControls()
    {
        string root = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string xaml = File.ReadAllText(
            Path.Combine(root, "src", "MailLogInspector.App", "AdminSettingsWindow.xaml"));

        Assert.Contains("Width=\"1060\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"720\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScrollViewer", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"AdminDefaultReportSyntaxRadioButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"AdminCustomReportSyntaxRadioButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("GroupName=\"SmtpReportSyntax\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"AdminDefaultReportSyntaxTextBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"AdminCustomReportSyntaxTextBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"AdminReportSyntaxExplanationTextBlock\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"AdminReportSyntaxPreviewTextBlock\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"AdminReportSyntaxValidationTextBlock\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"AdminSmtpPortalLastUsedTextBlock\"", xaml, StringComparison.Ordinal);
    }
}
