namespace MailLogInspector.Storage;

public static class ImapProvider
{
    public const string Gmail = "gmail";
    public const string Microsoft365 = "microsoft365";
    public const string Custom = "custom";

    public static string Normalize(string? value)
    {
        if (string.Equals(value, Microsoft365, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "outlook", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "office365", StringComparison.OrdinalIgnoreCase))
        {
            return Microsoft365;
        }

        return string.Equals(value, Custom, StringComparison.OrdinalIgnoreCase)
            ? Custom
            : Gmail;
    }
}

public sealed record ImapConnectionProfile(string Host, int Port, bool UseSsl);

public static class ImapProviderProfiles
{
    public static ImapConnectionProfile Resolve(
        string? provider,
        string? customHost,
        int? customPort,
        bool customUseSsl)
    {
        return ImapProvider.Normalize(provider) switch
        {
            ImapProvider.Microsoft365 => new ImapConnectionProfile("outlook.office365.com", 993, true),
            ImapProvider.Custom => new ImapConnectionProfile(
                (customHost ?? string.Empty).Trim(),
                customPort.GetValueOrDefault(customUseSsl ? 993 : 143),
                customUseSsl),
            _ => new ImapConnectionProfile("imap.gmail.com", 993, true)
        };
    }
}