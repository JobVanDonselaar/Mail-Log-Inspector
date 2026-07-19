namespace MailLogInspector.Storage;

public sealed record GmailReportHistoryRow(
    string GmailMessageId,
    DateTimeOffset GmailInternalDate,
    string Sender,
    string Subject,
    string ZipUrl,
    string DownloadStatus,
    string ImportStatus,
    bool Archived,
    string? ErrorText,
    DateTime FirstSeenAtUtc,
    DateTime LastAttemptAtUtc,
    string? SourceHash = null);
