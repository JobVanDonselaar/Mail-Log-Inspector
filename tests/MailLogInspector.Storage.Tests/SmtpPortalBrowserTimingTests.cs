using MailLogInspector.App;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class SmtpPortalBrowserTimingTests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("null", false)]
    public void ScriptBooleanParserTreatsNullAsNoMatch(string json, bool expected)
    {
        Assert.Equal(expected, SmtpPortalScriptResultParser.ParseBoolean(json));
    }

    [Fact]
    public void AuthenticationWaitsForCredentialNavigationBeforeRejectingLogin()
    {
        DateTime submittedAtUtc = new(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);

        Assert.True(SmtpPortalAuthenticationTiming.IsCredentialSubmissionPending(
            submittedAtUtc,
            submittedAtUtc.AddSeconds(5)));
        Assert.False(SmtpPortalAuthenticationTiming.IsCredentialSubmissionPending(
            submittedAtUtc,
            submittedAtUtc.AddSeconds(10)));
    }

    [Fact]
    public void AuthenticationWaitsBeforeTryingAnotherTotpWindow()
    {
        DateTime submittedAtUtc = new(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);

        Assert.True(SmtpPortalAuthenticationTiming.IsTotpSubmissionPending(
            submittedAtUtc,
            submittedAtUtc.AddSeconds(3)));
        Assert.False(SmtpPortalAuthenticationTiming.IsTotpSubmissionPending(
            submittedAtUtc,
            submittedAtUtc.AddSeconds(6)));
    }
}