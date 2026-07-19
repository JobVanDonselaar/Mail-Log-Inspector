namespace MailLogInspector.App;

public sealed record GmailOAuthTokenEnvelope(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc);
