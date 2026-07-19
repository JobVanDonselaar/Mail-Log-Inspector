using System.Windows;
using MailLogInspector.Core;
using MailLogInspector.Storage;

namespace MailLogInspector.App;

public partial class MainWindow
{
    private bool IsGmailConfigurationReady(GmailReportConfig config)
    {
        string mode = string.Equals(config.AuthenticationMode, GmailAuthenticationMode.AppPassword, StringComparison.OrdinalIgnoreCase)
            ? GmailAuthenticationMode.AppPassword
            : GmailAuthenticationMode.OAuth;
        if (string.IsNullOrWhiteSpace(config.AccountEmailAddress) ||
            (string.Equals(ImapProvider.Normalize(config.ImapProvider), ImapProvider.Custom, StringComparison.OrdinalIgnoreCase) &&
             string.IsNullOrWhiteSpace(config.ImapHost)))
        {
            return false;
        }

        return string.Equals(mode, GmailAuthenticationMode.AppPassword, StringComparison.OrdinalIgnoreCase)
            ? !string.IsNullOrWhiteSpace(config.EncryptedAppPassword)
            : !string.IsNullOrWhiteSpace(config.ClientId) &&
              !string.IsNullOrWhiteSpace(config.ClientSecret) &&
              !string.IsNullOrWhiteSpace(config.EncryptedRefreshToken);
    }

    private bool IsSmtpPortalConfigurationReady()
    {
        SmtpPortalConfig config = _smtpPortalOperationalStore.LoadConfig();
        return !string.IsNullOrWhiteSpace(config.Username) &&
               !string.IsNullOrWhiteSpace(config.EncryptedPassword) &&
               !string.IsNullOrWhiteSpace(config.EncryptedTotpSecret);
    }

    private bool IsReportSyncConfigurationReady(GmailReportConfig gmailConfig)
    {
        string mode = _reportSyncOperationalStore.LoadConfig().Mode;
        bool gmailReady = IsGmailConfigurationReady(gmailConfig);
        bool directReady = IsSmtpPortalConfigurationReady();
        return mode switch
        {
            ReportSyncMode.DirectOnly => directReady,
            ReportSyncMode.DirectWithGmailFallback => directReady || gmailReady,
            _ => gmailReady
        };
    }

    private void RefreshGmailSection()
    {
        RefreshBeheer();
    }

    private async void SyncGmailReportsButton_Click(object sender, RoutedEventArgs e)
    {
        await RunReportSyncAsync(automatic: false);
    }

    private void OpenAdminSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is not App app || !app.ShowAdminSettings(this))
        {
            return;
        }

        ReportSyncConfig config = _reportSyncOperationalStore.LoadConfig();
        ApplyGmailAutoSyncSchedule(config);
        RefreshBeheer();
    }

    private async Task RunGmailStartupSyncIfRequiredAsync(ReportSyncConfig config)
    {
        DateTime? latestReportDay = _store.ReadLatestDailyImportReportDayReadOnly();
        if (!ReportSyncSchedulePolicy.ShouldRunAutomatic(
                config.AutoSyncEnabled,
                _activeArchiveMonthKey,
                latestReportDay,
                DateTime.UtcNow) ||
            _gmailSyncIsRunning)
        {
            return;
        }

        await RunReportSyncAsync(automatic: true);
    }

    private void SetGmailSyncState(bool isRunning, string statusText)
    {
        _gmailSyncIsRunning = isRunning;
        StatusTextBlock.Text = statusText;
        ImportProgressBar.IsIndeterminate = isRunning;
        if (!isRunning)
        {
            ImportProgressBar.Value = 0.0;
        }
        ImportProgressTextBlock.Text = isRunning ? statusText : "Nog geen actieve import.";
        SyncCancelButton.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
        SyncCancelButton.IsEnabled = isRunning;
        SyncGmailReportsButton.IsEnabled = !isRunning &&
            _activeArchiveMonthKey == null &&
            IsReportSyncConfigurationReady(_gmailOperationalStore.LoadConfig());
    }

    private void SyncCancelButton_Click(object sender, RoutedEventArgs e)
    {
        SyncCancelButton.IsEnabled = false;
        ImportProgressTextBlock.Text = "Synchronisatie stoppen...";
        _syncCancellation?.Cancel();
    }

    private async void GmailAutoSyncTimer_Tick(object? sender, EventArgs e)
    {
        _gmailAutoSyncTimer.Interval = TimeSpan.FromMinutes(FixedGmailAutoSyncIntervalMinutes);
        if (_gmailSyncIsRunning || _activeArchiveMonthKey != null)
        {
            return;
        }

        ReportSyncConfig config = _reportSyncOperationalStore.LoadConfig();
        DateTime? latestReportDay = _store.ReadLatestDailyImportReportDayReadOnly();
        if (!ReportSyncSchedulePolicy.ShouldRunAutomatic(
                config.AutoSyncEnabled,
                _activeArchiveMonthKey,
                latestReportDay,
                DateTime.UtcNow))
        {
            return;
        }

        await RunReportSyncAsync(automatic: true);
    }

    private async Task RunReportSyncAsync(bool automatic)
    {
        if (_gmailSyncIsRunning)
        {
            return;
        }

        GmailReportConfig gmailConfig = _gmailOperationalStore.LoadConfig();
        ReportSyncConfig syncConfig = _reportSyncOperationalStore.LoadConfig();
        if (!IsReportSyncConfigurationReady(gmailConfig))
        {
            StatusTextBlock.Text = "De gekozen synchronisatiebron is niet volledig geconfigureerd. Open de instellingen via Help of /admin.";
            return;
        }

        string finalStatus = automatic ? "Automatische synchronisatie draait..." : "Synchronisatie draait...";
        var syncCancellation = new CancellationTokenSource();
        _syncCancellation = syncCancellation;
        SetGmailSyncState(true, finalStatus);
        try
        {
            DateTime? latestReportDay = _store.ReadLatestDailyImportReportDayReadOnly();
            ReportSyncSourceResult result = await _reportSyncCoordinator.RunAsync(
                syncConfig.Mode,
                latestOnly: !latestReportDay.HasValue,
                minimumReportDayExclusive: latestReportDay,
                syncCancellation.Token,
                new Progress<string>(message => ImportProgressTextBlock.Text = message));
            finalStatus = BuildReportSyncSummary(result, automatic);

            await RefreshDashboardAsync();
        }
        catch (OperationCanceledException) when (syncCancellation.IsCancellationRequested)
        {
            finalStatus = automatic
                ? "Automatische synchronisatie gestopt."
                : "Synchronisatie gestopt.";
        }
        catch (Exception ex)
        {
            finalStatus = (automatic ? "Automatische synchronisatie mislukt: " : "Synchronisatie mislukt: ") + ex.Message;
            MailLogInspectorLog.Error("sync", finalStatus, ex);
        }
        finally
        {
            if (ReferenceEquals(_syncCancellation, syncCancellation))
            {
                _syncCancellation = null;
                SetGmailSyncState(false, finalStatus);
                RefreshGmailSection();
            }
            syncCancellation.Dispose();
        }
    }

    private static string BuildReportSyncSummary(ReportSyncSourceResult result, bool automatic)
    {
        string prefix = automatic ? "Automatische sync gereed." : "Sync gereed.";
        return $"{prefix} {result.Summary}";
    }

    private void ApplyGmailAutoSyncSchedule(ReportSyncConfig config)
    {
        _closeToTrayEnabled = config.CloseToTrayEnabled;
        _gmailAutoSyncTimer.Interval = ReportSyncSchedulePolicy.DelayUntilNextQuarterHour(DateTime.Now);
        if (config.AutoSyncEnabled && _activeArchiveMonthKey == null)
        {
            _gmailAutoSyncTimer.Start();
        }
        else
        {
            _gmailAutoSyncTimer.Stop();
        }
    }
}