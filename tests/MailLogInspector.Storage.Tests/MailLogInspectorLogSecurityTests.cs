using MailLogInspector.Core;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class MailLogInspectorLogSecurityTests
{
    [Fact]
    public void Error_RedactsSensitiveValuesAndDoesNotWriteStackTrace()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "mail-log-security-" + Guid.NewGuid().ToString("N"));
        MailLogInspectorLog.Configure(root);
        var exception = new InvalidOperationException(
            "request failed: access_token=secret-token&code=secret-code");

        MailLogInspectorLog.Error(
            "security-test",
            "client_secret=secret-client",
            exception);

        string log = File.ReadAllText(
            Path.Combine(root, "Logs", "mail-log-inspector.log"));
        Assert.DoesNotContain("secret-token", log, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-code", log, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-client", log, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", log, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", log, StringComparison.Ordinal);
    }
}
