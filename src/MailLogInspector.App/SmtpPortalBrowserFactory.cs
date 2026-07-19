namespace MailLogInspector.App;

public sealed class SmtpPortalBrowserFactory : ISmtpPortalBrowserFactory
{
    public ISmtpPortalBrowser Create(string userDataFolder)
    {
        return new SmtpPortalBrowserWindow(userDataFolder);
    }
}
