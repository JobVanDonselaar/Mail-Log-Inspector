using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MailLogInspector.Core;
using MailLogInspector.Storage;

namespace MailLogInspector.App;

/// <summary>
/// Koppelt het bouncemeldings-systeem aan de importflow: na elke import wordt per
/// afzender-e-mailadres bepaald wie een overzicht van de gebouncede mails krijgt.
/// </summary>
public partial class MainWindow
{
    private bool _bounceNotificationWindowOpen;

    private BounceNotificationService CreateBounceNotificationService()
    {
        return new BounceNotificationService(
            _store,
            _bounceNotificationStore,
            settings => BounceMailTransportFactory.Create(
                settings,
                _gmailOperationalStore,
                new GmailOAuthService()),
            Path.Combine(Path.GetTempPath(), "MailLogInspector", "bounce-notifications"));
    }

    /// <summary>
    /// Bouwt het meldingsoverzicht voor de opgegeven import en toont het venster, of verstuurt
    /// direct als automatisch versturen aanstaat. Fouten mogen de import nooit laten mislukken.
    /// </summary>
    private async Task RunBounceNotificationsAfterImportAsync(long importId, string? sourceFileName)
    {
        if (importId <= 0 || _activeArchiveMonthKey != null)
        {
            return;
        }

        try
        {
            BounceNotificationService service = CreateBounceNotificationService();

            IReadOnlyList<BounceNotificationPlanItem> plan =
                await Task.Run(() => service.BuildPlan(importId));

            if (plan.Count == 0)
            {
                MailLogInspectorLog.Info(
                    "bounce-notify",
                    $"Import={importId} | Geen bounces gevonden, geen meldingen voorbereid");
                return;
            }

            BounceNotificationSettings settings = _bounceNotificationStore.LoadSettings();

            if (settings.AutoSendAfterImport)
            {
                await SendBounceNotificationsAutomaticallyAsync(service, plan, sourceFileName);
                return;
            }

            ShowBounceNotificationWindow(service, plan, sourceFileName);
        }
        catch (Exception ex)
        {
            MailLogInspectorLog.Error(
                "bounce-notify",
                $"Import={importId} | Voorbereiden van bouncemeldingen mislukt",
                ex);
            StatusTextBlock.Text = "Bouncemeldingen konden niet worden voorbereid: " + ex.Message;
        }
    }

    private async Task SendBounceNotificationsAutomaticallyAsync(
        BounceNotificationService service,
        IReadOnlyList<BounceNotificationPlanItem> plan,
        string? sourceFileName)
    {
        List<BounceNotificationPlanItem> sendable = plan.Where(item => item.IsSendable).ToList();
        if (sendable.Count == 0)
        {
            MailLogInspectorLog.Info(
                "bounce-notify",
                "Automatisch versturen aan, maar geen enkele afzender staat aan");
            return;
        }

        IReadOnlyList<BounceNotificationSendResult> results = await service.SendAsync(
            sendable,
            DateTime.Today,
            sourceFileName,
            CancellationToken.None);

        int ok = results.Count(result => result.Success);
        int failed = results.Count - ok;

        MailLogInspectorLog.Info(
            "bounce-notify",
            $"Automatisch verstuurd | Geslaagd={ok} | Mislukt={failed}");

        StatusTextBlock.Text = failed == 0
            ? $"{ok} bouncemelding(en) automatisch verstuurd."
            : $"{ok} bouncemelding(en) verstuurd, {failed} mislukt. Zie het logboek.";
    }

    private void ShowBounceNotificationWindow(
        BounceNotificationService service,
        IReadOnlyList<BounceNotificationPlanItem> plan,
        string? sourceFileName)
    {
        if (_bounceNotificationWindowOpen)
        {
            return;
        }

        _bounceNotificationWindowOpen = true;
        try
        {
            var window = new BounceNotificationWindow(
                _bounceNotificationStore,
                service,
                plan,
                DateTime.Today,
                sourceFileName)
            {
                Owner = IsLoaded && IsVisible ? this : null
            };

            window.ShowDialog();
        }
        finally
        {
            _bounceNotificationWindowOpen = false;
        }
    }

    /// <summary>Opent het meldingsoverzicht voor de laatste import, ook zonder nieuwe import.</summary>
    private async Task ShowBounceNotificationsForLatestImportAsync()
    {
        try
        {
            long importId = await Task.Run(() => _store.ReadLatestImportId() ?? 0L);
            if (importId <= 0)
            {
                StatusTextBlock.Text = "Er is nog geen import om bouncemeldingen voor te maken.";
                return;
            }

            BounceNotificationService service = CreateBounceNotificationService();
            IReadOnlyList<BounceNotificationPlanItem> plan =
                await Task.Run(() => service.BuildPlan(importId));

            if (plan.Count == 0)
            {
                StatusTextBlock.Text = "Geen bounces gevonden in de laatste import.";
                return;
            }

            ShowBounceNotificationWindow(service, plan, sourceFileName: null);
        }
        catch (Exception ex)
        {
            MailLogInspectorLog.Error("bounce-notify", "Openen van het meldingsoverzicht mislukt", ex);
            StatusTextBlock.Text = "Bouncemeldingen openen mislukt: " + ex.Message;
        }
    }

    private void BounceNotificationsButton_Click(object sender, RoutedEventArgs e)
    {
        _ = ShowBounceNotificationsForLatestImportAsync();
    }
}
