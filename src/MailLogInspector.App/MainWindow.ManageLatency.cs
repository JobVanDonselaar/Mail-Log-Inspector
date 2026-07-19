using MailLogInspector.Core;

namespace MailLogInspector.App;

public partial class MainWindow
{
    private sealed record DeliveryLatencyTrendPoint(
        DateTime Date,
        string DayAxisLabel,
        int DeliveredCount,
        double BarHeight,
        System.Windows.Media.Brush BarBrush,
        string ToolTip);

    private sealed record DeliveryLatencyTrendData(
        IReadOnlyList<DeliveryLatencyTrendPoint> Points,
        int LatestDelivered,
        string LatestDeliveredDisplay,
        string LatestAverageDurationDisplay,
        string LatestP95Display,
        string LatestDateDisplay,
        string YAxisMaximumDisplay,
        string YAxisMidpointDisplay,
        string SummaryText);

    private void ApplyDeliveryLatencyTrend(
        IReadOnlyList<MailLogInspectorDeliveryLatencyDay> days,
        bool metricsPending)
    {
        DeliveryLatencyTrendData trend = BuildDeliveryLatencyTrend(days, metricsPending);
        DeliveredThirtyDayTrendItemsControl.ItemsSource = trend.Points;
        DeliveredThirtyDayLatestTextBlock.Text = trend.LatestDateDisplay;
        DeliveredThirtyDayLatestValueTextBlock.Text = trend.LatestDeliveredDisplay;
        DeliveredThirtyDayAverageTextBlock.Text = trend.LatestAverageDurationDisplay;
        DeliveredThirtyDayLowestTextBlock.Text = trend.LatestP95Display;
        DeliveredThirtyDayYAxisMaximumTextBlock.Text = trend.YAxisMaximumDisplay;
        DeliveredThirtyDayYAxisMidpointTextBlock.Text = trend.YAxisMidpointDisplay;
        DeliveredThirtyDaySummaryTextBlock.Text = trend.SummaryText;
    }

    private static DeliveryLatencyTrendData BuildDeliveryLatencyTrend(
        IReadOnlyList<MailLogInspectorDeliveryLatencyDay> days,
        bool metricsPending)
    {
        List<MailLogInspectorDeliveryLatencyDay> ordered = days
            .OrderBy(day => day.Date)
            .TakeLast(30)
            .ToList();
        if (ordered.Count == 0)
        {
            return new DeliveryLatencyTrendData(
                Array.Empty<DeliveryLatencyTrendPoint>(),
                0,
                "-",
                "-",
                "-",
                string.Empty,
                "-",
                "-",
                metricsPending
                    ? "Afleversnelheid wordt eenmalig opgebouwd..."
                    : "Geen afgeleverde mails met duurgegevens beschikbaar.");
        }

        int maxDelivered = Math.Max(1, ordered.Max(day => day.DeliveredCount));
        int middleIndex = ordered.Count / 2;
        List<DeliveryLatencyTrendPoint> points = ordered
            .Select((day, index) => new DeliveryLatencyTrendPoint(
                day.Date,
                IsDayAxisLabel(index, middleIndex, ordered.Count)
                    ? day.Date.ToString("dd-MM", MailLogInspectorDisplayFormats.Culture)
                    : string.Empty,
                day.DeliveredCount,
                CalculateTrendBarHeight(day.DeliveredCount, maxDelivered),
                CreateBrush("#2E8B57"),
                BuildLatencyTooltip(day)))
            .ToList();

        MailLogInspectorDeliveryLatencyDay latest = ordered[^1];
        long durationCount = ordered.Sum(day => (long)day.DurationCount);
        long durationSum = ordered.Sum(day => day.DurationSumSeconds);
        long withinFive = ordered.Sum(day => (long)day.Within300Count);
        long withinFifteen = ordered.Sum(day => (long)day.Within900Count);
        string averageDisplay = FormatDuration(latest.DurationCount <= 0
            ? null
            : (int)Math.Round(latest.DurationSumSeconds / (double)latest.DurationCount));
        string p95Display = DescribeP95Band(latest);
        string summary = durationCount <= 0
            ? $"{ordered.Count} dagen | geen bruikbare duurgegevens."
            : $"{ordered.Count} dagen | gemiddeld {FormatDuration((int)Math.Round(durationSum / (double)durationCount))} | " +
              $"{FormatPercent(withinFive, durationCount)} binnen 5 min | " +
              $"{FormatPercent(durationCount - withinFifteen, durationCount)} langer dan 15 min.";

        return new DeliveryLatencyTrendData(
            points,
            latest.DeliveredCount,
            FormatCompactCount(latest.DeliveredCount),
            averageDisplay,
            p95Display,
            $"t/m {latest.Date:dd-MM-yyyy}",
            FormatCompactCount(maxDelivered),
            FormatCompactCount((int)Math.Ceiling(maxDelivered / 2.0)),
            summary);
    }


    private static bool IsDayAxisLabel(int index, int middleIndex, int count)
    {
        return index == 0 || index == middleIndex || index == count - 1;
    }
    private static string BuildLatencyTooltip(MailLogInspectorDeliveryLatencyDay day)
    {
        string average = FormatDuration(day.DurationCount <= 0
            ? null
            : (int)Math.Round(day.DurationSumSeconds / (double)day.DurationCount));
        string withinFive = FormatPercent(day.Within300Count, day.DurationCount);
        string missing = FormatPercent(day.MissingDurationCount, day.DeliveredCount);
        return $"{day.Date:dd-MM-yyyy}: {day.DeliveredCount:n0} afgeleverd | gemiddeld {average} | " +
               $"95% binnen {DescribeP95Band(day)} | {withinFive} binnen 5 min | {missing} zonder duur";
    }

    private static string DescribeP95Band(MailLogInspectorDeliveryLatencyDay day)
    {
        if (day.DurationCount <= 0)
        {
            return "-";
        }

        int target = (int)Math.Ceiling(day.DurationCount * 0.95);
        if (day.Within60Count >= target)
        {
            return "1 min";
        }
        if (day.Within300Count >= target)
        {
            return "5 min";
        }
        if (day.Within900Count >= target)
        {
            return "15 min";
        }
        return day.Within3600Count >= target ? "1 uur" : "> 1 uur";
    }

    private static string FormatDuration(int? seconds)
    {
        if (!seconds.HasValue)
        {
            return "-";
        }

        TimeSpan duration = TimeSpan.FromSeconds(Math.Max(0, seconds.Value));
        if (duration.TotalMinutes < 1)
        {
            return $"{Math.Round(duration.TotalSeconds):0}s";
        }
        if (duration.TotalHours < 1)
        {
            return $"{(int)duration.TotalMinutes}m {duration.Seconds:00}s";
        }
        return $"{(int)duration.TotalHours}u {duration.Minutes:00}m";
    }

    private static string FormatPercent(long count, long total)
    {
        return total <= 0
            ? "-"
            : (count * 100.0 / total).ToString("0.0", MailLogInspectorDisplayFormats.Culture) + "%";
    }

    private async Task EnsureDeliveryLatencyAggregatesAsync()
    {
        if (_activeArchiveMonthKey != null)
        {
            return;
        }

        try
        {
            bool rebuilt = await Task.Run(() =>
            {
                using IDisposable measurement = MailLogInspectorLog.Measure("analysis", "Afleversnelheid eenmalig opbouwen");
                return _store.EnsureDeliveryLatencyAggregates();
            });
            if (rebuilt)
            {
                await RefreshDashboardAsync(invalidateDataViews: false);
            }
        }
        catch (Exception ex)
        {
            MailLogInspectorLog.Error("analysis", "Afleversnelheid kon niet worden opgebouwd", ex);
        }
    }
}
