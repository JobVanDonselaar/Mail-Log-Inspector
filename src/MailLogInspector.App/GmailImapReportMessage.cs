namespace MailLogInspector.App;

public sealed record GmailImapReportMessage(
    string GmailMessageId,
    DateTimeOffset InternalDate,
    string Sender,
    string Subject,
    string? HtmlBody,
    string? TextBody,
    string MessageUniqueId,
    string SourceMailbox = "INBOX");
