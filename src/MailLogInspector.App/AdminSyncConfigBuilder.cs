using MailLogInspector.Storage;

namespace MailLogInspector.App;

public sealed record AdminSyncSettingsInput(
    string Mode,
    bool AutoSyncEnabled,
    bool CloseToTrayEnabled);

public static class AdminSyncConfigBuilder
{
    public static ReportSyncConfig Build(
        ReportSyncConfig current,
        AdminSyncSettingsInput input)
    {
        return current with
        {
            Mode = ReportSyncMode.Normalize(input.Mode),
            AutoSyncEnabled = input.AutoSyncEnabled,
            CloseToTrayEnabled = input.CloseToTrayEnabled
        };
    }
}