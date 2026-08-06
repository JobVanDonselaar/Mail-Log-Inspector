namespace MailLogInspector.Storage;

public sealed record ReportSyncConfig(
    string Mode,
    DateTime? LastAttemptAtUtc,
    DateTime? LastSuccessAtUtc,
    bool AutoSyncEnabled = false,
    bool CloseToTrayEnabled = false,
    bool PortalSyncVisible = false)
{
    public static ReportSyncConfig Default { get; } =
        new(ReportSyncMode.Default, null, null, false, false, false);
}