namespace MailLogInspector.App;

public sealed record GmailOAuthConfig(
    string AccountEmailAddress,
    string ClientId,
    string ClientSecret,
    string? RefreshToken);
