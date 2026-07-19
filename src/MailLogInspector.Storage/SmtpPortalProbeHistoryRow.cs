namespace MailLogInspector.Storage;

public sealed record SmtpPortalProbeHistoryRow(
    string ReportName,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    string SourceHash,
    string LocalPath,
    long FileSize,
    bool AlreadyImported,
    string Status,
    string? ErrorText,
    DateTime AttemptedAtUtc);
