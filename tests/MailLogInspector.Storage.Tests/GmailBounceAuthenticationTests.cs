using MailLogInspector.App;
using MailLogInspector.Storage;
using Xunit;

namespace MailLogInspector.Storage.Tests;

/// <summary>
/// Bouncemeldingen gaan via dezelfde Gmail-koppeling als de rapportsynchronisatie. Werkt die
/// koppeling met een app-wachtwoord, dan moet verzenden ook met een app-wachtwoord werken.
/// </summary>
public sealed class GmailBounceAuthenticationTests
{
    private static GmailReportConfig WithAppPassword() => GmailReportConfig.Empty with
    {
        AccountEmailAddress = "mailloginspector@gmail.com",
        AuthenticationMode = GmailAuthenticationMode.AppPassword,
        EncryptedAppPassword = "versleuteld-app-wachtwoord"
    };

    private static GmailReportConfig WithOAuth() => GmailReportConfig.Empty with
    {
        AccountEmailAddress = "mailloginspector@gmail.com",
        AuthenticationMode = GmailAuthenticationMode.OAuth,
        ClientId = "client-id",
        ClientSecret = "versleuteld-secret",
        EncryptedRefreshToken = "versleutelde-token"
    };

    [Fact]
    public void AppPasswordIsEnoughToSend()
    {
        Assert.Equal(
            GmailBounceAuthenticationPlan.Method.AppPassword,
            GmailBounceAuthenticationPlan.Resolve(WithAppPassword()));
    }

    [Fact]
    public void OAuthKeepsWorkingAsBefore()
    {
        Assert.Equal(
            GmailBounceAuthenticationPlan.Method.OAuth,
            GmailBounceAuthenticationPlan.Resolve(WithOAuth()));
    }

    [Fact]
    public void MissingOAuthCredentialsAreNotDemandedWhenAnAppPasswordIsUsed()
    {
        GmailReportConfig config = WithAppPassword();

        Assert.Null(config.ClientId);
        Assert.Null(config.EncryptedRefreshToken);
        Assert.Equal(
            GmailBounceAuthenticationPlan.Method.AppPassword,
            GmailBounceAuthenticationPlan.Resolve(config));
    }

    [Fact]
    public void MissingAppPasswordIsReportedClearly()
    {
        GmailReportConfig config = WithAppPassword() with { EncryptedAppPassword = null };

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => GmailBounceAuthenticationPlan.Resolve(config));

        Assert.Contains("app-wachtwoord ontbreekt", error.Message, StringComparison.Ordinal);
        Assert.Contains("IMAP-instellingen", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IncompleteOAuthIsStillReported()
    {
        GmailReportConfig config = WithOAuth() with { EncryptedRefreshToken = null };

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => GmailBounceAuthenticationPlan.Resolve(config));

        Assert.Contains("OAuth-gegevens zijn onvolledig", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingAccountIsReportedBeforeAnythingElse()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => GmailBounceAuthenticationPlan.Resolve(GmailReportConfig.Empty));

        Assert.Contains("geen Gmail-account", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMailboxOnMicrosoftIsNotSentThroughGmail()
    {
        GmailReportConfig config = WithAppPassword() with
        {
            ImapProvider = ImapProvider.Microsoft365,
            AccountEmailAddress = "rapport@bedrijf.nl"
        };

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => GmailBounceAuthenticationPlan.Resolve(config));

        Assert.Contains("staat niet op Gmail", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyAuthenticationModeFallsBackToOAuth()
    {
        GmailReportConfig config = WithOAuth() with { AuthenticationMode = null };

        Assert.Equal(
            GmailBounceAuthenticationPlan.Method.OAuth,
            GmailBounceAuthenticationPlan.Resolve(config));
    }

    [Fact]
    public void TheTransportNoLongerClaimsToBeOAuthOnly()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string xaml = File.ReadAllText(Path.Combine(root, "src", "MailLogInspector.App", "MainWindow.xaml"));

        Assert.Contains("Gmail (bestaande IMAP-gegevens)", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Gmail (bestaande OAuth-gegevens)", xaml, StringComparison.Ordinal);
    }
}
