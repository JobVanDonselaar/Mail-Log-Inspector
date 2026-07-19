using MailLogInspector.App;
using System.Reflection;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class SmtpPortalCookieConsentTests
{
    [Fact]
    public void ConsentScriptRejectsWithoutAcceptingTracking()
    {
        string script = SmtpPortalCookieConsentScript.RejectAll;

        Assert.Contains("Reject All", script, StringComparison.Ordinal);
        Assert.Contains("Decline All", script, StringComparison.Ordinal);
        Assert.DoesNotContain("I Accept", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Accept All", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BrowserTracksThatConsentWasAlreadyHandled()
    {
        FieldInfo? field = typeof(SmtpPortalBrowserWindow).GetField(
            "_cookieConsentHandled",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        Assert.Equal(typeof(bool), field!.FieldType);
    }
}
