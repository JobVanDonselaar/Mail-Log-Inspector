using System.Globalization;

namespace MailLogInspector.App;

public sealed record SmtpPortalReportRow(string Name, string Status, string RowKey);

public sealed record SmtpPortalReport(
    string Name,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    string RowKey);

public static class SmtpPortalReportMatcher
{
    public static bool TryParse(string name, string status, out SmtpPortalReport? report, string rowKey = "")
    {
        return TryParse(
            name,
            status,
            SmtpPortalReportNameSyntax.DefaultTemplate,
            out report,
            rowKey);
    }

    public static bool TryParse(
        string name,
        string status,
        string reportNameTemplate,
        out SmtpPortalReport? report,
        string rowKey = "")
    {
        report = null;
        if (!string.Equals(status?.Trim(), "Ready", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string normalizedName = name?.Trim() ?? string.Empty;
        System.Text.RegularExpressions.Match match =
            SmtpPortalReportNameSyntax.BuildRegex(reportNameTemplate).Match(normalizedName);
        if (!match.Success ||
            !DateTime.TryParseExact(match.Groups["start"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime start) ||
            !DateTime.TryParseExact(match.Groups["end"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime end) ||
            end <= start)
        {
            return false;
        }

        report = new SmtpPortalReport(normalizedName, start, end, rowKey);
        return true;
    }

    public static SmtpPortalReport SelectNewest(IEnumerable<SmtpPortalReportRow> rows)
    {
        return SelectNewest(rows, SmtpPortalReportNameSyntax.DefaultTemplate);
    }

    public static SmtpPortalReport SelectNewest(
        IEnumerable<SmtpPortalReportRow> rows,
        string reportNameTemplate)
    {
        SmtpPortalReport? newest = rows
            .Select(row => TryParse(
                row.Name,
                row.Status,
                reportNameTemplate,
                out SmtpPortalReport? report,
                row.RowKey)
                    ? report
                    : null)
            .OfType<SmtpPortalReport>()
            .OrderByDescending(report => report.PeriodStart)
            .ThenByDescending(report => report.PeriodEnd)
            .FirstOrDefault();

        return newest ?? throw new InvalidOperationException("Geen gereed SMTP.com-dagrapport gevonden op de eerste pagina.");
    }

    public static IReadOnlyList<SmtpPortalReport> SelectRequired(
        IEnumerable<SmtpPortalReportRow> rows,
        DateTime? latestReportDay,
        DateTime yesterday,
        bool latestOnly)
    {
        return SelectRequired(
            rows,
            latestReportDay,
            yesterday,
            latestOnly,
            SmtpPortalReportNameSyntax.DefaultTemplate);
    }

    public static IReadOnlyList<SmtpPortalReport> SelectRequired(
        IEnumerable<SmtpPortalReportRow> rows,
        DateTime? latestReportDay,
        DateTime yesterday,
        bool latestOnly,
        string reportNameTemplate)
    {
        SmtpPortalReport[] available = rows
            .Select(row => TryParse(
                row.Name,
                row.Status,
                reportNameTemplate,
                out SmtpPortalReport? report,
                row.RowKey)
                    ? report
                    : null)
            .OfType<SmtpPortalReport>()
            .Where(report => report.PeriodStart.Date <= yesterday.Date)
            .GroupBy(report => report.PeriodStart.Date)
            .Select(group => group.OrderByDescending(report => report.PeriodEnd).First())
            .OrderBy(report => report.PeriodStart)
            .ToArray();

        if (latestOnly || !latestReportDay.HasValue)
        {
            SmtpPortalReport? newest = available.LastOrDefault();
            return newest is null ? [] : [newest];
        }

        return available
            .Where(report => report.PeriodStart.Date > latestReportDay.Value.Date)
            .ToArray();
    }
}