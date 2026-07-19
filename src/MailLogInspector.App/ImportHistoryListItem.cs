namespace MailLogInspector.App;

public sealed record ImportHistoryListItem(
    DateTime Timestamp,
    string Source,
    string FileName,
    string ReportPeriod,
    int? MailCount,
    int? DeliveredCount,
    int? BounceCount,
    int? UnderwayCount,
    string Status,
    string? ErrorText)
{
    public string MailCountDisplay => FormatCount(MailCount);

    public string DeliveredCountDisplay => FormatCount(DeliveredCount);

    public string BounceCountDisplay => FormatCount(BounceCount);

    public string UnderwayCountDisplay => FormatCount(UnderwayCount);

    private static string FormatCount(int? value)
    {
        return value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-";
    }
}
