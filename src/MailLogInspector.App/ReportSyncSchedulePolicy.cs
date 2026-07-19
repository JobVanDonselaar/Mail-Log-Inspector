namespace MailLogInspector.App;

public static class ReportSyncSchedulePolicy
{
    private static readonly TimeSpan FirstAttemptLocalTime = TimeSpan.FromHours(1);

    public static TimeSpan DelayUntilNextQuarterHour(DateTime localNow)
    {
        DateTime hourStart = new(
            localNow.Year,
            localNow.Month,
            localNow.Day,
            localNow.Hour,
            0,
            0,
            localNow.Kind);
        int nextQuarterMinutes = ((localNow.Minute / 15) + 1) * 15;
        DateTime nextQuarter = hourStart.AddMinutes(nextQuarterMinutes);
        return nextQuarter - localNow;
    }

    public static bool ShouldRunAutomatic(
        bool enabled,
        string? activeArchiveMonthKey,
        DateTime? latestReportDay,
        DateTime utcNow,
        TimeZoneInfo? localTimeZone = null)
    {
        if (!enabled || !string.IsNullOrWhiteSpace(activeArchiveMonthKey))
        {
            return false;
        }

        TimeZoneInfo timeZone = localTimeZone ?? TimeZoneInfo.Local;
        DateTime localNow = TimeZoneInfo.ConvertTimeFromUtc(NormalizeUtc(utcNow), timeZone);
        if (localNow.TimeOfDay < FirstAttemptLocalTime)
        {
            return false;
        }

        DateTime requiredReportDay = localNow.Date.AddDays(-1);
        return !latestReportDay.HasValue || latestReportDay.Value.Date < requiredReportDay;
    }

    private static DateTime NormalizeUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    }
}
