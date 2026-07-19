namespace MailLogInspector.Storage;

public static class ReportSyncMode
{
    public const string DirectWithGmailFallback = "direct-with-gmail-fallback";
    public const string GmailOnly = "gmail-only";
    public const string DirectOnly = "direct-only";

    public static string Normalize(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            DirectWithGmailFallback => DirectWithGmailFallback,
            DirectOnly => DirectOnly,
            _ => GmailOnly
        };
    }
}

public static class ReportImportSource
{
    public const string SmtpDirect = "SMTP.com direct";
    public const string Gmail = "Gmail";
    public const string Imap = "IMAP";
    public const string Manual = "Handmatig";

    public static string FromImapProvider(string? provider)
    {
        return string.Equals(ImapProvider.Normalize(provider), ImapProvider.Gmail, StringComparison.OrdinalIgnoreCase)
            ? Gmail
            : Imap;
    }
}
