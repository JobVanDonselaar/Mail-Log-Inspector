namespace MailLogInspector.Storage;

/// <summary>Verzendkanaal voor bouncemeldingen.</summary>
public static class BounceNotificationTransport
{
    /// <summary>Gmail/Google Workspace via XOAUTH2 op smtp.gmail.com. Gebruikt de bestaande synchronisatie-inloggegevens.</summary>
    public const string Gmail = "gmail";

    /// <summary>SMTP.com relay met de channel-inloggegevens.</summary>
    public const string SmtpRelay = "smtp-relay";

    /// <summary>Microsoft 365 / Exchange Online via SMTP AUTH.</summary>
    public const string Microsoft365 = "microsoft365";

    public const string Default = Gmail;

    public static string Normalize(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            SmtpRelay => SmtpRelay,
            Microsoft365 => Microsoft365,
            Gmail => Gmail,
            _ => Default
        };
    }
}

/// <summary>Algemene instellingen voor het versturen van bouncemeldingen.</summary>
public sealed record BounceNotificationSettings(
    bool Enabled,
    bool AutoSendAfterImport,
    string Transport,
    string? FromAddress,
    string? FromDisplayName,
    string? SubjectTemplate,
    string? RelayHost,
    int RelayPort,
    string? RelayUsername,
    string? EncryptedRelayPassword,
    BounceNotificationContentOptions Content)
{
    public const string DefaultSubjectTemplate = "Bounce-overzicht {sender} - {date}";

    public static BounceNotificationSettings Default { get; } = new(
        Enabled: false,
        AutoSendAfterImport: false,
        Transport: BounceNotificationTransport.Default,
        FromAddress: null,
        FromDisplayName: "Mail Log Inspector",
        SubjectTemplate: DefaultSubjectTemplate,
        RelayHost: null,
        RelayPort: 587,
        RelayUsername: null,
        EncryptedRelayPassword: null,
        Content: BounceNotificationContentOptions.Default);

    public string ResolveSubjectTemplate() =>
        string.IsNullOrWhiteSpace(SubjectTemplate) ? DefaultSubjectTemplate : SubjectTemplate.Trim();

    /// <summary>De inhoudsopties met de garantie dat er altijd iets te melden valt.</summary>
    public BounceNotificationContentOptions ResolveContent() =>
        (Content ?? BounceNotificationContentOptions.Default).EnsureNotEmpty();
}

/// <summary>Notificatie-instelling voor één afzender-e-mailadres.</summary>
public sealed record BounceNotificationSender(
    string SenderAddress,
    bool Enabled,
    string? RecipientOverride,
    DateTime? LastNotifiedAtUtc,
    int LastNotifiedBounceCount)
{
    public static BounceNotificationSender CreateDisabled(string senderAddress) =>
        new(senderAddress, Enabled: false, RecipientOverride: null, LastNotifiedAtUtc: null, LastNotifiedBounceCount: 0);
}
