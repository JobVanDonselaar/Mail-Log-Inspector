namespace MailLogInspector.App;

public sealed record GmailZipImportOutcome(
    bool Success,
    string SourceHash,
    DateTime? ReportStart = null,
    DateTime? ReportEnd = null,
    bool AlreadyImported = false);
