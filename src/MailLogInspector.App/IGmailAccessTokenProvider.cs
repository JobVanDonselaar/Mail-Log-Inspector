namespace MailLogInspector.App;

public interface IGmailAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(GmailOAuthConfig config, CancellationToken cancellationToken);
}
