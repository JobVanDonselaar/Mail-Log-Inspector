namespace MailLogInspector.App;

public sealed record SmtpPortalProbeResult(
    string ReportName,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    string LocalPath,
    string Sha256,
    long FileSize,
    bool AlreadyImported);
