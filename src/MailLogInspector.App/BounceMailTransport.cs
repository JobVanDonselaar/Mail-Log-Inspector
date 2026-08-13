using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using MailLogInspector.Core;
using MailLogInspector.Storage;
using MimeKit;
using MimeKit.Utils;

namespace MailLogInspector.App;

/// <summary>Eén te versturen bouncemelding, inclusief eventuele Excel-bijlage.</summary>
public sealed record BounceNotificationMessage(
    string ToAddress,
    string Subject,
    string? HtmlBody,
    string? PlainTextBody,
    string? AttachmentPath,
    string? AttachmentFileName,
    string? BccAddress = null);

/// <summary>Verzendkanaal voor bouncemeldingen. Elke implementatie levert één transportmethode.</summary>
public interface IBounceMailTransport
{
    string Name { get; }

    Task SendAsync(BounceNotificationMessage message, CancellationToken cancellationToken);
}

/// <summary>Bouwt het MIME-bericht dat elk transport verstuurt.</summary>
public static class BounceNotificationMimeBuilder
{
    /// <summary>
    /// Leest het afzenderadres uit wat de gebruiker heeft ingevuld. Zowel een kaal adres,
    /// een adres met spaties eromheen als de vorm "Naam &lt;adres&gt;" levert hetzelfde
    /// resultaat op; zonder deze stap loopt het verzenden vast op een onbegrijpelijke
    /// parseerfout uit de mailbibliotheek.
    /// </summary>
    public static bool TryReadSenderAddress(string? fromAddress, out string address, out string? displayName)
    {
        address = string.Empty;
        displayName = null;

        if (string.IsNullOrWhiteSpace(fromAddress))
        {
            return false;
        }

        if (!MailboxAddress.TryParse(fromAddress.Trim(), out MailboxAddress? mailbox) ||
            string.IsNullOrWhiteSpace(mailbox.Address))
        {
            return false;
        }

        // MimeKit accepteert ook een adres zonder domein. Dat komt nooit aan, dus hier weigeren
        // levert een begrijpelijke melding in plaats van een mislukte verzending.
        int separator = mailbox.Address.LastIndexOf('@');
        if (separator <= 0 || separator == mailbox.Address.Length - 1)
        {
            return false;
        }

        address = mailbox.Address;
        displayName = string.IsNullOrWhiteSpace(mailbox.Name) ? null : mailbox.Name;
        return true;
    }

    /// <summary>
    /// Haalt het domein uit een afzenderadres. Dat domein hoort in de Message-Id en in de
    /// HELO-naam; zonder dat vult MimeKit de Windows-machinenaam in, wat spamfilters als een
    /// niet-bestaand domein zien en zwaar laten meewegen.
    /// </summary>
    public static string ResolveSendingDomain(string? fromAddress)
    {
        if (TryReadSenderAddress(fromAddress, out string address, out _))
        {
            int separator = address.LastIndexOf('@');
            if (separator >= 0 && separator < address.Length - 1)
            {
                string domain = address[(separator + 1)..];
                if (domain.Length > 0 && domain.Contains('.'))
                {
                    return domain;
                }
            }
        }

        return "localhost.localdomain";
    }

    /// <summary>
    /// Zet de afmeldheader. Zonder een geldig afzenderadres blijft die weg: een afmeldroute
    /// beloven die nergens aankomt, telt bij ontvangende filters juist als negatief signaal.
    /// Alleen de mailto-variant, want One-Click afmelden vereist volgens RFC 8058 een
    /// https-adres dat de afmelding zelf verwerkt en dat is er niet.
    /// </summary>
    private static void AddUnsubscribeHeader(MimeMessage mime, string address)
    {
        mime.Headers.Add(HeaderId.ListUnsubscribe, $"<mailto:{address}?subject=Afmelden>");
    }

    public static MimeMessage Build(
        BounceNotificationMessage message,
        string fromAddress,
        string? fromDisplayName)
    {
        if (!TryReadSenderAddress(fromAddress, out string senderAddress, out string? addressDisplayName))
        {
            throw new InvalidOperationException(
                $"Het afzenderadres '{fromAddress}' is geen geldig e-mailadres. Pas dit aan bij de e-mailinstellingen.");
        }

        string name = fromDisplayName?.Trim() is { Length: > 0 } configured
            ? configured
            : addressDisplayName ?? "Mail Log Inspector";

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(name, senderAddress));
        mime.To.Add(MailboxAddress.Parse(message.ToAddress));
        AddBccRecipient(mime, message.BccAddress);
        mime.Subject = message.Subject;
        mime.MessageId = MimeUtils.GenerateMessageId(ResolveSendingDomain(senderAddress));

        // Meldt dat dit een automatisch rapport is: dat onderdrukt afwezigheidsantwoorden en
        // voorkomt dat filters het als ongevraagde handmatige post beoordelen.
        mime.Headers.Add(HeaderId.AutoSubmitted, "auto-generated");

        // Een zichtbare afmeldroute laat ontvangende filters minder streng oordelen. Gmail toont
        // hierdoor een afmeldknop naast de afzender. De mailto wijst naar het afzenderadres zelf,
        // dat ook in de afsluitende tekst als afmeldadres wordt genoemd.
        AddUnsubscribeHeader(mime, senderAddress);

        var builder = new BodyBuilder();

        if (!string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            builder.HtmlBody = message.HtmlBody;

            if (message.HtmlBody.Contains(
                    BounceNotificationHeaderLogo.ContentSource,
                    StringComparison.Ordinal))
            {
                MimeEntity logo = builder.LinkedResources.Add(
                    BounceNotificationHeaderLogo.FileName,
                    BounceNotificationHeaderLogo.Bytes,
                    new ContentType("image", "png"));
                logo.ContentId = BounceNotificationHeaderLogo.ContentId;
            }
        }

        if (!string.IsNullOrWhiteSpace(message.PlainTextBody))
        {
            builder.TextBody = message.PlainTextBody;
        }

        if (builder.HtmlBody is null && builder.TextBody is null)
        {
            builder.TextBody = message.Subject;
        }

        if (!string.IsNullOrWhiteSpace(message.AttachmentPath) && File.Exists(message.AttachmentPath))
        {
            builder.Attachments.Add(
                string.IsNullOrWhiteSpace(message.AttachmentFileName)
                    ? Path.GetFileName(message.AttachmentPath)
                    : message.AttachmentFileName,
                File.ReadAllBytes(message.AttachmentPath),
                new ContentType("application", "vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
        }

        mime.Body = builder.ToMessageBody();
        return mime;
    }

    private static void AddBccRecipient(MimeMessage mime, string? bccAddress)
    {
        if (string.IsNullOrWhiteSpace(bccAddress))
        {
            return;
        }

        if (!MailboxAddress.TryParse(bccAddress.Trim(), out MailboxAddress? mailbox) ||
            mailbox is null ||
            string.IsNullOrWhiteSpace(mailbox.Address) ||
            !MailLogInspectorNotificationAddressPolicy.IsPlausibleAddress(mailbox.Address))
        {
            throw new InvalidOperationException(
                $"Het BCC-adres '{bccAddress}' is geen geldig e-mailadres. Pas dit aan bij de e-mailinstellingen.");
        }

        mime.Bcc.Add(mailbox);
    }
}

/// <summary>
/// Bepaalt hoe er bij Gmail wordt aangemeld: met het app-wachtwoord of via OAuth. De keuze volgt
/// de aanmeldmethode van de IMAP-koppeling, zodat verzenden werkt zodra die koppeling werkt.
/// </summary>
public static class GmailBounceAuthenticationPlan
{
    public enum Method
    {
        AppPassword,
        OAuth
    }

    /// <summary>
    /// Controleert de configuratie en meldt welke aanmeldmethode bruikbaar is. Bij een onbruikbare
    /// configuratie volgt een uitleg die verwijst naar het scherm waar het opgelost wordt.
    /// </summary>
    public static Method Resolve(GmailReportConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.AccountEmailAddress))
        {
            throw new InvalidOperationException(
                "Er is geen Gmail-account ingesteld. Configureer dit eerst bij de IMAP-instellingen.");
        }

        if (!string.Equals(ImapProvider.Normalize(config.ImapProvider), ImapProvider.Gmail, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "De IMAP-koppeling staat niet op Gmail. Kies bij Verzendinstellingen het transport dat bij die mailbox hoort.");
        }

        if (string.Equals(config.AuthenticationMode, GmailAuthenticationMode.AppPassword, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(config.EncryptedAppPassword))
            {
                throw new InvalidOperationException(
                    "Het Gmail app-wachtwoord ontbreekt. Vul dit eerst in bij de IMAP-instellingen.");
            }

            return Method.AppPassword;
        }

        if (string.IsNullOrWhiteSpace(config.ClientId) ||
            string.IsNullOrWhiteSpace(config.ClientSecret) ||
            string.IsNullOrWhiteSpace(config.EncryptedRefreshToken))
        {
            throw new InvalidOperationException(
                "De Gmail OAuth-gegevens zijn onvolledig. Voltooi eerst de autorisatie bij de IMAP-instellingen.");
        }

        return Method.OAuth;
    }
}

/// <summary>
/// Verstuurt via smtp.gmail.com met de gegevens die al voor de rapportsynchronisatie zijn
/// ingesteld. Zowel een Gmail app-wachtwoord als Google OAuth werkt; welke van de twee wordt
/// gebruikt volgt de aanmeldmethode van de IMAP-koppeling.
/// </summary>
public sealed class GmailBounceMailTransport : IBounceMailTransport
{
    private const string SmtpHost = "smtp.gmail.com";
    private const int SmtpPort = 587;

    private const string ImapHost = "imap.gmail.com";
    private const int ImapPort = 993;

    private readonly GmailReportOperationalStore _gmailStore;
    private readonly IGmailAccessTokenProvider _tokenProvider;
    private readonly BounceNotificationSettings _settings;
    private readonly IGmailImapReportClient _imapClient;

    public GmailBounceMailTransport(
        GmailReportOperationalStore gmailStore,
        IGmailAccessTokenProvider tokenProvider,
        BounceNotificationSettings settings,
        IGmailImapReportClient imapClient)
    {
        _gmailStore = gmailStore;
        _tokenProvider = tokenProvider;
        _settings = settings;
        _imapClient = imapClient;
    }

    public string Name => "Gmail";

    public async Task SendAsync(BounceNotificationMessage message, CancellationToken cancellationToken)
    {
        GmailReportConfig config = _gmailStore.LoadConfig();
        GmailBounceAuthenticationPlan.Method method = GmailBounceAuthenticationPlan.Resolve(config);

        MimeMessage mime = BounceNotificationMimeBuilder.Build(
            message,
            string.IsNullOrWhiteSpace(_settings.FromAddress) ? config.AccountEmailAddress! : _settings.FromAddress!,
            _settings.FromDisplayName);

        string? accessToken = null;
        if (method == GmailBounceAuthenticationPlan.Method.OAuth)
        {
            accessToken = await _tokenProvider.GetAccessTokenAsync(
                new GmailOAuthConfig(
                    config.AccountEmailAddress!,
                    config.ClientId!,
                    GmailOAuthService.UnprotectClientSecret(config.ClientSecret!),
                    GmailOAuthService.UnprotectRefreshToken(config.EncryptedRefreshToken!)),
                cancellationToken);
        }

        SaslMechanism authentication = method == GmailBounceAuthenticationPlan.Method.AppPassword
            ? new SaslMechanismLogin(
                config.AccountEmailAddress!,
                GmailOAuthService.UnprotectSecret(config.EncryptedAppPassword!))
            : new SaslMechanismOAuth2(config.AccountEmailAddress!, accessToken!);

        using var client = new SmtpClient
        {
            LocalDomain = BounceNotificationMimeBuilder.ResolveSendingDomain(mime.From.Mailboxes.First().Address)
        };
        await client.ConnectAsync(SmtpHost, SmtpPort, SecureSocketOptions.StartTls, cancellationToken);
        await client.AuthenticateAsync(authentication, cancellationToken);
        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);

        if (_settings.ClearGmailSentItemsAfterSend)
        {
            GmailImapConnectionSettings imapSettings = method == GmailBounceAuthenticationPlan.Method.AppPassword
                ? new GmailImapConnectionSettings(
                    config.AccountEmailAddress!,
                    GmailAuthenticationMode.AppPassword,
                    null,
                    GmailOAuthService.UnprotectSecret(config.EncryptedAppPassword!),
                    ImapHost,
                    ImapPort,
                    UseSsl: true,
                    ImapProvider: ImapProvider.Gmail)
                : new GmailImapConnectionSettings(
                    config.AccountEmailAddress!,
                    GmailAuthenticationMode.OAuth,
                    accessToken!,
                    null,
                    ImapHost,
                    ImapPort,
                    UseSsl: true,
                    ImapProvider: ImapProvider.Gmail);

            await _imapClient.ClearSentFolderAsync(imapSettings, cancellationToken);
        }
    }
}

/// <summary>
/// SMTP-relay met gebruikersnaam en wachtwoord. Geschikt voor de SMTP.com relay
/// (smtp.smtp.com) en voor Microsoft 365 / Exchange Online (smtp.office365.com).
/// </summary>
public sealed class SmtpRelayBounceMailTransport : IBounceMailTransport
{
    private readonly BounceNotificationSettings _settings;

    public SmtpRelayBounceMailTransport(BounceNotificationSettings settings)
    {
        _settings = settings;
    }

    public string Name => _settings.Transport == BounceNotificationTransport.Microsoft365
        ? "Microsoft 365 (SMTP)"
        : "SMTP-relay";

    public async Task SendAsync(BounceNotificationMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.RelayHost))
        {
            throw new InvalidOperationException("Er is geen SMTP-server ingesteld voor dit verzendkanaal.");
        }

        if (string.IsNullOrWhiteSpace(_settings.FromAddress))
        {
            throw new InvalidOperationException("Er is geen afzenderadres ingesteld voor dit verzendkanaal.");
        }

        MimeMessage mime = BounceNotificationMimeBuilder.Build(
            message,
            _settings.FromAddress!,
            _settings.FromDisplayName);

        using var client = new SmtpClient
        {
            LocalDomain = BounceNotificationMimeBuilder.ResolveSendingDomain(_settings.FromAddress)
        };
        await client.ConnectAsync(
            _settings.RelayHost,
            _settings.RelayPort <= 0 ? 587 : _settings.RelayPort,
            SecureSocketOptions.StartTls,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(_settings.RelayUsername) &&
            !string.IsNullOrWhiteSpace(_settings.EncryptedRelayPassword))
        {
            string password = SmtpPortalSecretProtector.Unprotect(_settings.EncryptedRelayPassword!);
            await client.AuthenticateAsync(_settings.RelayUsername, password, cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}

/// <summary>Kiest het transport dat bij de opgeslagen instellingen hoort.</summary>
public static class BounceMailTransportFactory
{
    public static IBounceMailTransport Create(
        BounceNotificationSettings settings,
        GmailReportOperationalStore gmailStore,
        IGmailAccessTokenProvider tokenProvider,
        IGmailImapReportClient? imapClient = null)
    {
        return BounceNotificationTransport.Normalize(settings.Transport) switch
        {
            BounceNotificationTransport.SmtpRelay => new SmtpRelayBounceMailTransport(settings),
            BounceNotificationTransport.Microsoft365 => new SmtpRelayBounceMailTransport(settings),
            _ => new GmailBounceMailTransport(gmailStore, tokenProvider, settings, imapClient ?? new GmailImapReportClient())
        };
    }
}
