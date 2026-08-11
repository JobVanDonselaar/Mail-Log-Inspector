namespace MailLogInspector.App;

public interface IGmailImapReportClient
{
    Task<IReadOnlyList<GmailImapReportMessage>> FetchInboxCandidatesAsync(GmailImapConnectionSettings settings, CancellationToken cancellationToken);

    Task<IReadOnlyList<GmailImapReportMessage>> FetchCatchupCandidatesAsync(GmailImapConnectionSettings settings, DateTime sinceUtc, CancellationToken cancellationToken);

    Task DeleteMessagePermanentlyAsync(GmailImapConnectionSettings settings, GmailImapReportMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// Verwijdert alle berichten uit de Verzonden-map. Bedoeld voor gebruik nadat bouncemeldingen
    /// zijn verstuurd, zodat de map leeg blijft en geen onnodige opslagruimte in beslag neemt.
    /// </summary>
    Task ClearSentFolderAsync(GmailImapConnectionSettings settings, CancellationToken cancellationToken);
}
