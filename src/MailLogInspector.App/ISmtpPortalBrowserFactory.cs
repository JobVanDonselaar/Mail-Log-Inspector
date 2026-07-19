namespace MailLogInspector.App;

public interface ISmtpPortalBrowserFactory
{
    ISmtpPortalBrowser Create(string userDataFolder);
}
