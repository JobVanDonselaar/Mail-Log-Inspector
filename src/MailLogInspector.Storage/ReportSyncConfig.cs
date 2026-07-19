namespace MailLogInspector.Storage;

public sealed record ReportSyncConfig(
    string Mode,
    DateTime? LastAttemptAtUtc,
    DateTime? LastSuccessAtUtc,
    bool AutoSyncEnabled = false,
    bool CloseToTrayEnabled = false)
{
    public static ReportSyncConfig Default { get; } =
        new(ReportSyncMode.GmailOnly, null, null, false, false);
}