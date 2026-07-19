using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MailLogInspector.Core;
using MailLogInspector.Storage;

namespace MailLogInspector.App;

public partial class MainWindow
{
    private MailLogInspectorSenderDomainDashboard? _cachedSenderDomainDashboard;
    private SenderDomainDashboardCacheKey? _cachedSenderDomainDashboardKey;
    private bool _senderDomainAnalyticsReady;

    private enum SearchRunReason
    {
        FreshSearch,
        LoadMore,
        StatusChange
    }

    private sealed record SenderDomainDashboardCacheKey(
        string DatabasePath,
        DateTime FromDate,
        DateTime ThroughDate,
        string SenderDomain);

    private sealed record SenderDomainTrendBar(double Height, string ToolTip);
    private sealed record SenderDomainCauseBar(string Label, string Count, double Width);
    private sealed record SenderDurationDelayBar(string Label, string Summary, double Width, string Color);

    private void SearchDashboardInputs_Changed(object sender, TextChangedEventArgs e)
    {
        UpdateSenderDomainDashboardOptionState();
    }

    private void SenderDomainDashboardOption_Changed(object sender, RoutedEventArgs e)
    {
        if (SenderDomainDashboardCheckBox.IsChecked == true
            && GetDashboardForCurrentSearch() is { } dashboard)
        {
            BindSenderDomainDashboard(dashboard);
            return;
        }

        ApplySenderDomainDashboardLayout(false);
    }

    private void UpdateSenderDomainDashboardOptionState()
    {
        MailLogInspectorSearchCriteria criteria = BuildSearchCriteria();
        bool eligible = _senderDomainAnalyticsReady
            && criteria.SenderDomain is not null
            && criteria.Sender is null
            && criteria.Recipient is null
            && criteria.RecipientDomain is null;

        SenderDomainDashboardCheckBox.IsEnabled = eligible;
        if (eligible && SenderDomainDashboardCheckBox.IsChecked == true && GetDashboardForCurrentSearch() is null)
        {
            ApplySenderDomainDashboardLayout(false);
        }
        if (!eligible)
        {
            ApplySenderDomainDashboardLayout(false);
        }
    }

    private bool TryBuildSenderDomainDashboardKey(
        MailLogInspectorSearchCriteria criteria,
        out SenderDomainDashboardCacheKey? key)
    {
        key = null;
        if (criteria.SenderDomain is null
            || criteria.Sender is not null
            || criteria.Recipient is not null
            || criteria.RecipientDomain is not null)
        {
            return false;
        }

        key = new SenderDomainDashboardCacheKey(
            Path.GetFullPath(_store.DatabasePath).ToUpperInvariant(),
            criteria.FromInclusive.Date,
            criteria.ThroughInclusive.Date,
            criteria.SenderDomain.Trim().TrimStart('@').ToLowerInvariant());
        return true;
    }

    private async Task RefreshSenderDomainDashboardForFreshSearchAsync(
        MailLogInspectorSearchCriteria criteria,
        CancellationToken cancellationToken)
    {
        if (SenderDomainDashboardCheckBox.IsChecked != true
            || !TryBuildSenderDomainDashboardKey(criteria, out SenderDomainDashboardCacheKey? key)
            || key is null)
        {
            ApplySenderDomainDashboardLayout(false);
            return;
        }

        if (key == _cachedSenderDomainDashboardKey && _cachedSenderDomainDashboard is not null)
        {
            BindSenderDomainDashboard(_cachedSenderDomainDashboard);
            return;
        }

        try
        {
            MailLogInspectorSenderDomainDashboard dashboard = await Task.Run(
                () => new MailLogInspectorSenderDomainDashboardService(_store)
                    .ReadSenderDomainDashboard(criteria, cancellationToken: cancellationToken),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _cachedSenderDomainDashboardKey = key;
            _cachedSenderDomainDashboard = dashboard;
            BindSenderDomainDashboard(dashboard);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            MailLogInspectorLog.Error("search", "Domeinanalyse laden mislukt", ex);
            ApplySenderDomainDashboardLayout(false);
            SearchRunDetailTextBlock.Text = "Zoekresultaten geladen; domeinanalyse is niet beschikbaar.";
        }
    }

    private MailLogInspectorSenderDomainDashboard? GetDashboardForCurrentSearch()
    {
        if (SenderDomainDashboardCheckBox.IsChecked != true
            || !TryBuildSenderDomainDashboardKey(BuildSearchCriteria(), out SenderDomainDashboardCacheKey? key)
            || key != _cachedSenderDomainDashboardKey)
        {
            return null;
        }

        return _cachedSenderDomainDashboard;
    }

    private void BindSenderDomainDashboard(MailLogInspectorSenderDomainDashboard dashboard)
    {
        BindDurationDistribution(dashboard);
        SenderDomainAverageDurationTextBlock.Text = FormatDuration(
            dashboard.AverageDurationSeconds.HasValue
                ? (int)Math.Round(dashboard.AverageDurationSeconds.Value)
                : null);
        SenderDomainP95DurationTextBlock.Text = FormatDurationBucket(dashboard.P95DurationBucket);

        int rawTrendMaximum = dashboard.Trend.Count == 0 ? 0 : dashboard.Trend.Max(day => day.DeliveredCount);
        int trendMaximum = RoundTrendMaximum(rawTrendMaximum);
        SenderDomainTrendMaximumTextBlock.Text = FormatCompactCount(trendMaximum);
        SenderDomainTrendMiddleTextBlock.Text = FormatCompactCount(trendMaximum / 2);
        SenderDomainTrendFromTextBlock.Text = dashboard.Trend.Count == 0
            ? string.Empty
            : dashboard.Trend[0].Date.ToString("dd-MM", CultureInfo.InvariantCulture);
        SenderDomainTrendThroughTextBlock.Text = dashboard.Trend.Count == 0
            ? string.Empty
            : dashboard.Trend[^1].Date.ToString("dd-MM", CultureInfo.InvariantCulture);
        SenderDomainTrendItemsControl.ItemsSource = dashboard.Trend
            .Select(day => new SenderDomainTrendBar(
                trendMaximum <= 0 ? 2 : Math.Max(2, day.DeliveredCount * 108.0 / trendMaximum),
                BuildSenderDomainTrendTooltip(day)))
            .ToArray();

        int causeMaximum = dashboard.TopCauses.Count == 0 ? 0 : dashboard.TopCauses.Max(cause => cause.Count);
        SenderDomainCauseItemsControl.ItemsSource = dashboard.TopCauses
            .Select(cause => new SenderDomainCauseBar(
                cause.Description,
                FormatCompactCount(cause.Count),
                causeMaximum <= 0 ? 0 : Math.Max(4, cause.Count * 220.0 / causeMaximum)))
            .ToArray();

        ApplySenderDomainDashboardLayout(true);
    }

    private void BindDurationDistribution(MailLogInspectorSenderDomainDashboard dashboard)
    {
        MailLogInspectorDurationDistribution distribution = dashboard.DurationDistribution;
        int delayedCount = distribution.LongerThanOneMinute;
        SenderDurationWithinOneSummaryTextBlock.Text =
            FormatDurationPercent(distribution.WithinOneMinute, distribution.DurationCount);
        SenderDurationDelayedSummaryTextBlock.Text = distribution.DurationCount <= 0
            ? "-"
            : $"{FormatCompactCount(delayedCount)} van {FormatCompactCount(distribution.DurationCount)} " +
              $"({FormatDurationPercent(delayedCount, distribution.DurationCount)})";

        (string Label, int Count, string Color)[] delayedBuckets =
        [
            ("1–5 min", distribution.OneToFiveMinutes, "#D4A72C"),
            ("5–15 min", distribution.FiveToFifteenMinutes, "#E0A11A"),
            ("15–60 min", distribution.FifteenToSixtyMinutes, "#D97706"),
            ("> 1 uur", distribution.OverOneHour, "#C83B2B")
        ];
        int delayedMaximum = delayedBuckets.Max(static bucket => bucket.Count);
        SenderDurationDelayItemsControl.ItemsSource = delayedBuckets
            .Select(bucket => new SenderDurationDelayBar(
                bucket.Label,
                FormatDurationDelaySummary(bucket.Count, delayedCount, distribution.DurationCount),
                bucket.Count <= 0 || delayedMaximum <= 0
                    ? 0
                    : Math.Max(4, bucket.Count * 220.0 / delayedMaximum),
                bucket.Color))
            .ToArray();

        SenderDurationCoverageTextBlock.Text = dashboard.DeliveredCount <= 0
            ? "Nog geen afgeleverde mails met duurgegevens."
            : $"Duur beschikbaar voor {FormatCompactCount(distribution.DurationCount)} van " +
              $"{FormatCompactCount(dashboard.DeliveredCount)} afgeleverde mails " +
              $"({FormatDurationPercent(distribution.DurationCount, dashboard.DeliveredCount)}).";
    }

    private static string FormatDurationDelaySummary(int count, int delayedCount, int durationCount)
    {
        string delayedShare = delayedCount <= 0
            ? "0,0%"
            : FormatDurationPercent(count, delayedCount);
        return $"{FormatCompactCount(count)} | {delayedShare} vertraagd | " +
               $"{FormatDurationPercent(count, durationCount)} totaal";
    }
    private static string FormatDurationPercent(int count, int total)
    {
        return total <= 0
            ? "-"
            : (Math.Max(0, count) * 100.0 / total)
                .ToString("0.0", MailLogInspectorDisplayFormats.Culture) + "%";
    }

    private static int RoundTrendMaximum(int value)
    {
        return Math.Max(0, value);
    }

    private static string BuildSenderDomainTrendTooltip(MailLogInspectorSenderDomainTrendDay day)
    {
        string average = FormatDuration(day.AverageDurationSeconds.HasValue
            ? (int)Math.Round(day.AverageDurationSeconds.Value)
            : null);
        return $"{day.Date:dd-MM-yyyy}: {FormatCompactCount(day.DeliveredCount)} afgeleverd " +
               $"({FormatDurationPercent(day.DeliveredCount, day.TotalCount)}) | " +
               $"{FormatCompactCount(day.BounceCount)} bounce | " +
               $"{FormatCompactCount(day.UnderwayCount)} onderweg | " +
               $"gemiddeld {average} | 95% binnen {FormatDurationBucket(day.P95DurationBucket)}";
    }
    private static string FormatDurationBucket(MailLogInspectorDurationBucket? bucket) => bucket switch
    {
        MailLogInspectorDurationBucket.WithinOneMinute => "1 min",
        MailLogInspectorDurationBucket.WithinFiveMinutes => "5 min",
        MailLogInspectorDurationBucket.WithinFifteenMinutes => "15 min",
        MailLogInspectorDurationBucket.WithinOneHour => "1 uur",
        MailLogInspectorDurationBucket.OverOneHour => "> 1 uur",
        _ => "-"
    };

    private void InvalidateSenderDomainDashboard()
    {
        _cachedSenderDomainDashboard = null;
        _cachedSenderDomainDashboardKey = null;
        ApplySenderDomainDashboardLayout(false);
    }

    private void ApplySenderDomainDashboardLayout(bool visible)
    {
        if (visible)
        {
            Grid.SetColumnSpan(SearchResultsPanel, 3);
            SearchResultsPanel.Margin = new Thickness(0, 0, 12, 0);
            SenderDomainDashboardPanel.Visibility = Visibility.Visible;
            return;
        }

        Grid.SetColumnSpan(SearchResultsPanel, 5);
        SearchResultsPanel.Margin = new Thickness(0);
        SenderDomainDashboardPanel.Visibility = Visibility.Collapsed;
    }

    private async Task EnsureCurrentSenderDomainAnalyticsReadyAsync()
    {
        _senderDomainAnalyticsReady = false;
        UpdateSenderDomainDashboardOptionState();
        try
        {
            await Task.Run(() => _store.EnsureSenderDomainAnalyticsAggregates());
            _senderDomainAnalyticsReady = true;
        }
        catch (Exception ex)
        {
            MailLogInspectorLog.Error("storage", "Domeinaggregatie mislukt voor " + _store.DatabasePath, ex);
            SearchRunDetailTextBlock.Text = "Domeinanalyse is niet beschikbaar voor deze database.";
        }
        UpdateSenderDomainDashboardOptionState();
    }

    private async Task EnsureSenderDomainAnalyticsBackfillAsync()
    {
        _senderDomainAnalyticsReady = false;
        UpdateSenderDomainDashboardOptionState();
        var databasePaths = new List<string> { _workspace.DatabasePath };
        if (Directory.Exists(_workspace.ArchiveDatabaseDirectory))
        {
            databasePaths.AddRange(Directory.EnumerateFiles(
                _workspace.ArchiveDatabaseDirectory,
                "????-??.sqlite"));
        }

        _senderDomainAnalyticsReady = await Task.Run(() =>
        {
            bool activeDatabaseReady = false;
            foreach (string databasePath in databasePaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var store = new MailLogInspectorStore(databasePath);
                    store.Initialize();
                    store.EnsureSenderDomainAnalyticsAggregates();
                    if (string.Equals(databasePath, _workspace.DatabasePath, StringComparison.OrdinalIgnoreCase))
                    {
                        activeDatabaseReady = true;
                    }
                }
                catch (Exception ex)
                {
                    MailLogInspectorLog.Error("startup", "Domeinaggregatie mislukt voor " + databasePath, ex);
                }
            }
            return activeDatabaseReady;
        });
        UpdateSenderDomainDashboardOptionState();
        if (!_senderDomainAnalyticsReady)
        {
            SearchRunDetailTextBlock.Text = "Domeinanalyse kon niet worden voorbereid; gewone zoekresultaten blijven beschikbaar.";
        }
    }
}