using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MailLogInspector.Storage;
using MimeKit;

namespace MailLogInspector.App;

public sealed class GmailImapReportClient : IGmailImapReportClient
{
    public async Task<IReadOnlyList<GmailImapReportMessage>> FetchInboxCandidatesAsync(GmailImapConnectionSettings settings, CancellationToken cancellationToken)
    {
        using ImapClient client = await ConnectAsync(settings, cancellationToken);
        IMailFolder inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
        IList<UniqueId> uids = await inbox.SearchAsync(SearchQuery.FromContains("no-reply@smtp.com"), cancellationToken);
        return await ReadMessagesAsync(inbox, uids, "INBOX", cancellationToken);
    }

    public async Task<IReadOnlyList<GmailImapReportMessage>> FetchCatchupCandidatesAsync(GmailImapConnectionSettings settings, DateTime sinceUtc, CancellationToken cancellationToken)
    {
        using ImapClient client = await ConnectAsync(settings, cancellationToken);
        IMailFolder sourceFolder = ResolveCatchupFolder(client, settings.ImapProvider);
        await sourceFolder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
        IList<UniqueId> uids = await sourceFolder.SearchAsync(SearchQuery.FromContains("no-reply@smtp.com").And(SearchQuery.DeliveredAfter(sinceUtc)), cancellationToken);
        return await ReadMessagesAsync(sourceFolder, uids, sourceFolder.FullName, cancellationToken);
    }

    public async Task DeleteMessagePermanentlyAsync(GmailImapConnectionSettings settings, GmailImapReportMessage message, CancellationToken cancellationToken)
    {
        using ImapClient client = await ConnectAsync(settings, cancellationToken);
        IMailFolder sourceFolder = ResolveSourceFolder(client, message.SourceMailbox);
        await sourceFolder.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

        if (!uint.TryParse(message.MessageUniqueId, out uint uidValue))
        {
            throw new InvalidOperationException("Gmail-bericht heeft geen geldige IMAP-UID.");
        }

        UniqueId uid = new(uidValue);
        await sourceFolder.AddFlagsAsync(uid, MessageFlags.Deleted, silent: true, cancellationToken);
        await sourceFolder.ExpungeAsync(new[] { uid }, cancellationToken);
    }

    private static IMailFolder ResolveSourceFolder(ImapClient client, string sourceMailbox)
    {
        if (string.Equals(sourceMailbox, "INBOX", StringComparison.OrdinalIgnoreCase))
        {
            return client.Inbox;
        }

        return client.GetFolder(sourceMailbox)
            ?? throw new InvalidOperationException($"IMAP-map ontbreekt: {sourceMailbox}.");
    }

    private static IMailFolder ResolveCatchupFolder(ImapClient client, string imapProvider)
    {
        string provider = ImapProvider.Normalize(imapProvider);
        if (string.Equals(provider, ImapProvider.Gmail, StringComparison.OrdinalIgnoreCase))
        {
            return client.GetFolder(SpecialFolder.All) ?? client.Inbox;
        }

        if (string.Equals(provider, ImapProvider.Microsoft365, StringComparison.OrdinalIgnoreCase))
        {
            return client.GetFolder(SpecialFolder.Archive) ?? client.Inbox;
        }

        return client.Inbox;
    }

    private static async Task<ImapClient> ConnectAsync(GmailImapConnectionSettings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Host) || settings.Port is < 1 or > 65535)
        {
            throw new InvalidOperationException("De IMAP-server of poort is ongeldig.");
        }

        var client = new ImapClient();
        SecureSocketOptions socketOptions = settings.UseSsl
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTls;
        await client.ConnectAsync(settings.Host, settings.Port, socketOptions, cancellationToken);

        if (string.Equals(settings.AuthenticationMode, GmailAuthenticationMode.AppPassword, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(settings.AppPassword))
            {
                throw new InvalidOperationException("Er is nog geen IMAP-wachtwoord opgeslagen.");
            }

            await client.AuthenticateAsync(settings.AccountEmailAddress, settings.AppPassword, cancellationToken);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(settings.OAuthAccessToken))
            {
                throw new InvalidOperationException("Er is nog geen Gmail OAuth access token beschikbaar.");
            }

            await client.AuthenticateAsync(new SaslMechanismOAuth2(settings.AccountEmailAddress, settings.OAuthAccessToken), cancellationToken);
        }

        return client;
    }


    private static async Task<IReadOnlyList<GmailImapReportMessage>> ReadMessagesAsync(IMailFolder folder, IList<UniqueId> uids, string sourceMailbox, CancellationToken cancellationToken)
    {
        List<GmailImapReportMessage> messages = new();
        foreach (UniqueId uid in uids.OrderByDescending(static item => item.Id))
        {
            MimeMessage message = await folder.GetMessageAsync(uid, cancellationToken);
            string sender = message.From.Mailboxes.FirstOrDefault()?.Address ?? string.Empty;
            string? htmlBody = message.HtmlBody;
            string? textBody = message.TextBody;
            string messageId = message.MessageId ?? uid.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            messages.Add(new GmailImapReportMessage(
                messageId,
                message.Date,
                sender,
                message.Subject ?? string.Empty,
                htmlBody,
                textBody,
                uid.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                sourceMailbox));
        }

        return messages;
    }
}
