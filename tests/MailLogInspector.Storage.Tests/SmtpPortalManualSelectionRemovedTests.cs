using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class SmtpPortalManualSelectionRemovedTests
{
    [Fact]
    public void AdminDoesNotExposeManualReportSelection()
    {
        string root = FindRepositoryRoot();
        string xaml = File.ReadAllText(
            Path.Combine(root, "src", "MailLogInspector.App", "AdminSettingsWindow.xaml"));
        string code = File.ReadAllText(
            Path.Combine(root, "src", "MailLogInspector.App", "AdminSettingsWindow.xaml.cs"));

        Assert.DoesNotContain("Rapporten kiezen", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ChooseSmtpPortalReportsButton", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ChooseSmtpPortalReportsButton_Click", code, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        return Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }
}
