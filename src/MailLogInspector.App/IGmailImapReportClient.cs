namespace MailLogInspector.App;

public interface IGmailImapReportClient
{
    Task<IReadOnlyList<GmailImapReportMessage>> FetchInboxCandidatesAsync(GmailImapConnectionSettings settings, CancellationToken cancellationToken);

    Task<IReadOnlyList<GmailImapReportMessage>> FetchCatchupCandidatesAsync(GmailImapConnectionSettings settings, DateTime sinceUtc, CancellationToken cancellationToken);

    Task DeleteMessagePermanentlyAsync(GmailImapConnectionSettings settings, GmailImapReportMessage message, CancellationToken cancellationToken);
}
