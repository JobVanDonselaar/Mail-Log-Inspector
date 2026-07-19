using System;
using System.IO;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class SingleInstanceStartupTests
{
    private static string ReadAppCode()
    {
        string codePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "MailLogInspector.App", "App.cs"));
        return File.ReadAllText(codePath);
    }

    private static string ReadMainWindowCode()
    {
        string codePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "MailLogInspector.App", "MainWindow.Tray.cs"));
        return File.ReadAllText(codePath);
    }

    [Fact]
    public void App_PreventsSecondInstanceAndSignalsExistingWindow()
    {
        string appCode = ReadAppCode();
        string mainWindowCode = ReadMainWindowCode();

        Assert.Contains("SingleInstanceCoordinator", appCode, StringComparison.Ordinal);
        Assert.Contains("MutexName", appCode, StringComparison.Ordinal);
        Assert.Contains("NamedPipeClientStream", appCode, StringComparison.Ordinal);
        Assert.Contains("NamedPipeServerStream", appCode, StringComparison.Ordinal);
        Assert.Contains("TryAcquirePrimaryInstance", appCode, StringComparison.Ordinal);
        Assert.Contains("TrySignalExistingInstance", appCode, StringComparison.Ordinal);
        Assert.Contains("StartActivationListener", appCode, StringComparison.Ordinal);
        Assert.Contains("OnExit", appCode, StringComparison.Ordinal);
        Assert.Contains("RestoreFromExternalActivation", mainWindowCode, StringComparison.Ordinal);
        Assert.Contains("AppActivationRequest.Admin", appCode, StringComparison.Ordinal);
        Assert.Contains("writer.WriteLine(request", appCode, StringComparison.Ordinal);
        Assert.Contains("ShowAdminSettings", appCode, StringComparison.Ordinal);
    }
}
