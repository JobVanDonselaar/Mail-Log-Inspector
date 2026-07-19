namespace MailLogInspector.App;

public static class SmtpPortalPageSizePolicy
{
    public static int? Resolve(DateTime? latestSuccessfulReportDay, DateTime today)
    {
        if (!latestSuccessfulReportDay.HasValue)
        {
            return null;
        }

        int ageInCalendarDays = (today.Date - latestSuccessfulReportDay.Value.Date).Days;
        return ageInCalendarDays > 3 ? 100 : null;
    }
}
