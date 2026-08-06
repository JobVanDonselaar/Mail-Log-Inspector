namespace MailLogInspector.App;

public sealed record SmtpApiSelectedReport(
    string ReportId,
    string Name,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    string Url,
    int SyntaxSlot);

public static class SmtpApiReportMatcher
{
    public static IReadOnlyList<SmtpApiSelectedReport> ParseAvailable(
        IEnumerable<SmtpApiReport> reports,
        IReadOnlyList<string> templates,
        string? channel)
    {
        List<SmtpApiSelectedReport> parsed = [];
        foreach (SmtpApiReport report in reports ?? [])
        {
            if (!report.IsReady || !MatchesChannel(report, channel))
            {
                continue;
            }

            for (int slot = 0; slot < templates.Count; slot++)
            {
                // De API markeert een afgerond rapport als "done"; de gedeelde matcher verwacht "Ready".
                if (!SmtpPortalReportMatcher.TryParse(
                        report.Name,
                        "Ready",
                        templates[slot],
                        out SmtpPortalReport? matched,
                        report.ReportId) ||
                    matched is null)
                {
                    continue;
                }

                parsed.Add(new SmtpApiSelectedReport(
                    report.ReportId,
                    matched.Name,
                    matched.PeriodStart,
                    matched.PeriodEnd,
                    report.Url!,
                    slot + 1));
                break;
            }
        }

        return parsed
            .OrderByDescending(report => report.PeriodStart)
            .ThenByDescending(report => report.PeriodEnd)
            .ToArray();
    }

    public static IReadOnlyList<SmtpApiSelectedReport> SelectRequired(
        IEnumerable<SmtpApiReport> reports,
        IReadOnlyList<string> templates,
        string? channel,
        DateTime? latestReportDay,
        DateTime yesterday,
        bool latestOnly)
    {
        SmtpApiSelectedReport[] available = ParseAvailable(reports, templates, channel)
            .Where(report => report.PeriodStart.Date <= yesterday.Date)
            .GroupBy(report => report.PeriodStart.Date)
            .Select(group => group
                .OrderByDescending(report => report.PeriodEnd)
                .ThenBy(report => report.SyntaxSlot)
                .First())
            .OrderBy(report => report.PeriodStart)
            .ToArray();

        if (latestOnly || !latestReportDay.HasValue)
        {
            SmtpApiSelectedReport? newest = available.LastOrDefault();
            return newest is null ? [] : [newest];
        }

        return available
            .Where(report => report.PeriodStart.Date > latestReportDay.Value.Date)
            .ToArray();
    }

    private static bool MatchesChannel(SmtpApiReport report, string? channel)
    {
        string normalizedChannel = channel?.Trim() ?? string.Empty;
        return normalizedChannel.Length == 0 ||
               string.Equals(report.Channel?.Trim(), normalizedChannel, StringComparison.OrdinalIgnoreCase);
    }
}
