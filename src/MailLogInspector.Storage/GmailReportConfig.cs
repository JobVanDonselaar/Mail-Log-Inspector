namespace MailLogInspector.Storage;

public sealed record GmailReportConfig(
    string? AccountEmailAddress,
    string? AuthenticationMode,
    string? ClientId,
    string? ClientSecret,
    string? EncryptedRefreshToken,
    string? EncryptedAppPassword,
    DateTime? ConnectedAtUtc,
    DateTime? LastTokenRefreshAtUtc,
    string? ConnectionStatus,
    string ImapProvider = "gmail",
    string? ImapHost = "imap.gmail.com",
    int ImapPort = 993,
    bool ImapUseSsl = true)
{
    public static GmailReportConfig Empty { get; } = new(
        AccountEmailAddress: null,
        AuthenticationMode: GmailAuthenticationMode.OAuth,
        ClientId: null,
        ClientSecret: null,
        EncryptedRefreshToken: null,
        EncryptedAppPassword: null,
        ConnectedAtUtc: null,
        LastTokenRefreshAtUtc: null,
        ConnectionStatus: null,
        ImapProvider: MailLogInspector.Storage.ImapProvider.Gmail,
        ImapHost: "imap.gmail.com",
        ImapPort: 993,
        ImapUseSsl: true);

    public GmailReportConfig(
        string? accountEmailAddress,
        string? authenticationMode,
        string? clientId,
        string? clientSecret,
        string? encryptedRefreshToken,
        string? encryptedAppPassword,
        bool autoSyncEnabled,
        int autoSyncIntervalMinutes,
        DateTime? lastAutoSyncAtUtc,
        DateTime? connectedAtUtc,
        DateTime? lastTokenRefreshAtUtc,
        string? connectionStatus,
        bool closeToTrayEnabled = false,
        string imapProvider = "gmail",
        string? imapHost = "imap.gmail.com",
        int imapPort = 993,
        bool imapUseSsl = true)
        : this(
            accountEmailAddress,
            authenticationMode,
            clientId,
            clientSecret,
            encryptedRefreshToken,
            encryptedAppPassword,
            connectedAtUtc,
            lastTokenRefreshAtUtc,
            connectionStatus,
            imapProvider,
            imapHost,
            imapPort,
            imapUseSsl)
    {
        _ = autoSyncEnabled;
        _ = autoSyncIntervalMinutes;
        _ = lastAutoSyncAtUtc;
        _ = closeToTrayEnabled;
    }
}