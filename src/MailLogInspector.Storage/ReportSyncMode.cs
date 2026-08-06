namespace MailLogInspector.Storage;

public static class ReportSyncMode
{
    public const string DirectWithGmailFallback = "direct-with-gmail-fallback";
    public const string GmailOnly = "gmail-only";
    public const string DirectOnly = "direct-only";
    public const string ApiOnly = "api-only";
    public const string ApiWithImapFallback = "api-with-imap-fallback";

    public const string Default = ApiOnly;

    public static string Normalize(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            DirectWithGmailFallback => DirectWithGmailFallback,
            DirectOnly => DirectOnly,
            GmailOnly => GmailOnly,
            ApiWithImapFallback => ApiWithImapFallback,
            _ => ApiOnly
        };
    }

    public static bool UsesApi(string? value)
    {
        string normalized = Normalize(value);
        return normalized is ApiOnly or ApiWithImapFallback;
    }
}

public static class ReportImportSource
{
    public const string SmtpDirect = "SMTP.com direct";
    public const string SmtpApi = "SMTP.com API";
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
