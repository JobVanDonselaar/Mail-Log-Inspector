using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using MailLogInspector.Storage;

namespace MailLogInspector.App;

public partial class MainWindow
{
    private readonly ISmtpApiStatsService _smtpApiStatsService;

    // Auto-refresh timer
    private DispatcherTimer? _liveApiRefreshTimer;
    private CancellationTokenSource? _liveApiCts;

    // Geselecteerde periode (1=vandaag, 7=7 dagen, 30=30 dagen)
    private int _liveApiPeriodDays = 7;

    private void LiveApi_InitializeTab()
    {
        UpdateLiveApiPeriodButtons();
        RestartLiveApiRefreshTimer();
    }

    private void LiveApiPeriodToday_Click(object sender, RoutedEventArgs e)
    {
        _liveApiPeriodDays = 1;
        UpdateLiveApiPeriodButtons();
        _ = LoadLiveApiStatsAsync();
    }

    private void LiveApiPeriod7Days_Click(object sender, RoutedEventArgs e)
    {
        _liveApiPeriodDays = 7;
        UpdateLiveApiPeriodButtons();
        _ = LoadLiveApiStatsAsync();
    }

    private void LiveApiPeriod30Days_Click(object sender, RoutedEventArgs e)
    {
        _liveApiPeriodDays = 30;
        UpdateLiveApiPeriodButtons();
        _ = LoadLiveApiStatsAsync();
    }

    private void LiveApiRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _ = LoadLiveApiStatsAsync();
    }

    private void LiveApiAutoRefreshIntervalBox_LostFocus(object sender, RoutedEventArgs e)
    {
        RestartLiveApiRefreshTimer();
    }

    private void UpdateLiveApiPeriodButtons()
    {
        if (LiveApiPeriodTodayButton == null) return;

        LiveApiPeriodTodayButton.FontWeight = _liveApiPeriodDays == 1
            ? System.Windows.FontWeights.SemiBold : System.Windows.FontWeights.Normal;
        LiveApiPeriod7DaysButton.FontWeight = _liveApiPeriodDays == 7
            ? System.Windows.FontWeights.SemiBold : System.Windows.FontWeights.Normal;
        LiveApiPeriod30DaysButton.FontWeight = _liveApiPeriodDays == 30
            ? System.Windows.FontWeights.SemiBold : System.Windows.FontWeights.Normal;
    }

    private void RestartLiveApiRefreshTimer()
    {
        _liveApiRefreshTimer?.Stop();
        _liveApiRefreshTimer = null;

        if (LiveApiAutoRefreshIntervalBox == null) return;

        if (!int.TryParse(LiveApiAutoRefreshIntervalBox.Text, out int minutes) || minutes <= 0)
        {
            return;
        }

        _liveApiRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(minutes)
        };
        _liveApiRefreshTimer.Tick += (_, _) => _ = LoadLiveApiStatsAsync();
        _liveApiRefreshTimer.Start();
    }

    private async Task LoadLiveApiStatsAsync()
    {
        if (LiveApiStatsPanel == null) return;

        // API-sleutel ophalen
        SmtpApiConfig config = _smtpApiOperationalStore.LoadConfig();
        if (!config.HasApiKey)
        {
            SetLiveApiStatus("Geen API-sleutel geconfigureerd. Stel deze in via Admin → SMTP.com API.", isError: true);
            return;
        }

        string apiKey;
        try
        {
            apiKey = SmtpPortalSecretProtector.Unprotect(config.EncryptedApiKey!);
        }
        catch
        {
            SetLiveApiStatus("API-sleutel kon niet ontsleuteld worden.", isError: true);
            return;
        }

        // Annuleer vorige aanroep
        _liveApiCts?.Cancel();
        _liveApiCts = new CancellationTokenSource();
        CancellationToken ct = _liveApiCts.Token;

        SetLiveApiLoading(true);

        try
        {
            DateTimeOffset now = DateTimeOffset.Now;
            DateTimeOffset start = _liveApiPeriodDays == 1
                ? now.Date  // vandaag 00:00
                : now.AddDays(-_liveApiPeriodDays + 1).Date;
            DateTimeOffset end = now;

            SmtpApiStats stats = await _smtpApiStatsService.GetStatsAsync(apiKey, start, end, ct);

            if (ct.IsCancellationRequested) return;

            ApplyLiveApiStats(stats);
            LiveApiLastUpdatedTextBlock.Text = $"Bijgewerkt: {DateTime.Now:HH:mm:ss}";
            SetLiveApiStatus(string.Empty, isError: false);
        }
        catch (OperationCanceledException)
        {
            // Genegeerd
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
            {
                SetLiveApiStatus($"Fout: {ex.Message}", isError: true);
            }
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                SetLiveApiLoading(false);
            }
        }
    }

    private void ApplyLiveApiStats(SmtpApiStats stats)
    {
        LiveApiAcceptedTextBlock.Text = stats.Accepted.ToString("N0");
        LiveApiDeliveredTextBlock.Text = stats.Delivered.ToString("N0");
        LiveApiDeliveredPctTextBlock.Text = $"{stats.DeliveredPercent:N1}%";
        LiveApiFailedTextBlock.Text = stats.Failed.ToString("N0");
        LiveApiQueuedTextBlock.Text = stats.Queued.ToString("N0");
        LiveApiUnsubTextBlock.Text = stats.Unsubscribed.ToString("N0");
        LiveApiComplainedTextBlock.Text = stats.Complained.ToString("N0");
    }

    private void SetLiveApiLoading(bool loading)
    {
        LiveApiProgressBar.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        LiveApiRefreshButton.IsEnabled = !loading;
    }

    private void SetLiveApiStatus(string message, bool isError)
    {
        LiveApiStatusTextBlock.Text = message;
        LiveApiStatusTextBlock.Foreground = isError
            ? (System.Windows.Media.Brush)FindResource("DangerBrush")
            : (System.Windows.Media.Brush)FindResource("MutedTextBrush");
    }

    private void LiveApiTab_SelectionChanged()
    {
        // Laad stats als de tab voor het eerst wordt geopend
        if (LiveApiStatsPanel != null &&
            string.IsNullOrEmpty(LiveApiAcceptedTextBlock?.Text))
        {
            _ = LoadLiveApiStatsAsync();
        }
    }
}
