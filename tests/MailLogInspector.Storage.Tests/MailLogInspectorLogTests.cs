using MailLogInspector.Core;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class MailLogInspectorLogTests
{
    [Fact]
    public void Write_CreatesLocalLogFile()
    {
        string root = Path.Combine(Path.GetTempPath(), "mail-log-logging-" + Guid.NewGuid().ToString("N"));
        MailLogInspectorLog.Configure(root);

        MailLogInspectorLog.Info("test", "logregel");

        string logPath = Path.Combine(root, "Logs", "mail-log-inspector.log");
        Assert.True(File.Exists(logPath));
        Assert.Contains("[test] logregel", File.ReadAllText(logPath), StringComparison.Ordinal);
    }
}