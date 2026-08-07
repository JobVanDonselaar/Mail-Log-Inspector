using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using MailLogInspector.Storage;
using MimeKit;

namespace MailLogInspector.App;

/// <summary>Eén te versturen bouncemelding, inclusief eventuele Excel-bijlage.</summary>
public sealed record BounceNotificationMessage(
    string ToAddress,
    string Subject,
    string? HtmlBody,
    string? PlainTextBody,
    string? AttachmentPath,
    string? AttachmentFileName);

/// <summary>Verzendkanaal voor bouncemeldingen. Elke implementatie levert één transportmethode.</summary>
public interface IBounceMailTransport
{
    string Name { get; }

    Task SendAsync(BounceNotificationMessage message, CancellationToken cancellationToken);
}

/// <summary>Bouwt het MIME-bericht dat elk transport verstuurt.</summary>
public static class BounceNotificationMimeBuilder
{
    public static MimeMessage Build(
        BounceNotificationMessage message,
        string fromAddress,
        string? fromDisplayName)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(
            string.IsNullOrWhiteSpace(fromDisplayName) ? "Mail Log Inspector" : fromDisplayName.Trim(),
            fromAddress));
        mime.To.Add(MailboxAddress.Parse(message.ToAddress));
        mime.Subject = message.Subject;

        var builder = new BodyBuilder();

        if (!string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            builder.HtmlBody = message.HtmlBody;
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
}

/// <summary>
/// Proof of concept-transport: verstuurt via smtp.gmail.com met de OAuth-gegevens die al voor
/// de rapportsynchronisatie zijn ingesteld.
/// </summary>
public sealed class GmailBounceMailTransport : IBounceMailTransport
{
    private const string SmtpHost = "smtp.gmail.com";
    private const int SmtpPort = 587;

    private readonly GmailReportOperationalStore _gmailStore;
    private readonly IGmailAccessTokenProvider _tokenProvider;
    private readonly BounceNotificationSettings _settings;

    public GmailBounceMailTransport(
        GmailReportOperationalStore gmailStore,
        IGmailAccessTokenProvider tokenProvider,
        BounceNotificationSettings settings)
    {
        _gmailStore = gmailStore;
        _tokenProvider = tokenProvider;
        _settings = settings;
    }

    public string Name => "Gmail (OAuth)";

    public async Task SendAsync(BounceNotificationMessage message, CancellationToken cancellationToken)
    {
        GmailReportConfig config = _gmailStore.LoadConfig();
        if (string.IsNullOrWhiteSpace(config.AccountEmailAddress))
        {
            throw new InvalidOperationException(
                "Er is geen Gmail-account ingesteld. Configureer dit eerst bij de IMAP-instellingen.");
        }

        if (string.IsNullOrWhiteSpace(config.ClientId) ||
            string.IsNullOrWhiteSpace(config.ClientSecret) ||
            string.IsNullOrWhiteSpace(config.EncryptedRefreshToken))
        {
            throw new InvalidOperationException(
                "De Gmail OAuth-gegevens zijn onvolledig. Voltooi eerst de autorisatie bij de IMAP-instellingen.");
        }

        var oauthConfig = new GmailOAuthConfig(
            config.AccountEmailAddress,
            config.ClientId,
            GmailOAuthService.UnprotectClientSecret(config.ClientSecret),
            GmailOAuthService.UnprotectRefreshToken(config.EncryptedRefreshToken));

        string accessToken = await _tokenProvider.GetAccessTokenAsync(oauthConfig, cancellationToken);

        string fromAddress = string.IsNullOrWhiteSpace(_settings.FromAddress)
            ? config.AccountEmailAddress
            : _settings.FromAddress!;

        MimeMessage mime = BounceNotificationMimeBuilder.Build(message, fromAddress, _settings.FromDisplayName);

        using var client = new SmtpClient();
        await client.ConnectAsync(SmtpHost, SmtpPort, SecureSocketOptions.StartTls, cancellationToken);
        await client.AuthenticateAsync(
            new SaslMechanismOAuth2(config.AccountEmailAddress, accessToken),
            cancellationToken);
        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
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

        using var client = new SmtpClient();
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
        IGmailAccessTokenProvider tokenProvider)
    {
        return BounceNotificationTransport.Normalize(settings.Transport) switch
        {
            BounceNotificationTransport.SmtpRelay => new SmtpRelayBounceMailTransport(settings),
            BounceNotificationTransport.Microsoft365 => new SmtpRelayBounceMailTransport(settings),
            _ => new GmailBounceMailTransport(gmailStore, tokenProvider, settings)
        };
    }
}
