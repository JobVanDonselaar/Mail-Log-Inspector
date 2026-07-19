namespace MailLogInspector.App;

public sealed record GmailImapConnectionSettings(
    string AccountEmailAddress,
    string AuthenticationMode,
    string? OAuthAccessToken,
    string? AppPassword,
    string Host = "imap.gmail.com",
    int Port = 993,
    bool UseSsl = true,
    string ImapProvider = "gmail");