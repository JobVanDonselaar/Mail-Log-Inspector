using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MailLogInspector.Core;
using MailLogInspector.Storage;

namespace MailLogInspector.App;

/// <summary>Eén afzender die klaarstaat om een bouncemelding te ontvangen.</summary>
public sealed record BounceNotificationPlanItem(
    MailLogInspectorSenderBounceReport Report,
    BounceNotificationSender Setting,
    string SuggestedRecipient)
{
    /// <summary>Het adres waar de melding daadwerkelijk heen gaat.</summary>
    public string EffectiveRecipient =>
        string.IsNullOrWhiteSpace(Setting.RecipientOverride)
            ? SuggestedRecipient
            : Setting.RecipientOverride!.Trim();

    public bool IsSendable =>
        Setting.Enabled &&
        !Setting.NeverNotify &&
        Report.BounceCount > 0 &&
        MailLogInspectorNotificationAddressPolicy.IsPlausibleAddress(EffectiveRecipient);
}

/// <summary>Resultaat van één verzendpoging.</summary>
public sealed record BounceNotificationSendResult(
    string SenderAddress,
    string Recipient,
    bool Success,
    string? ErrorMessage);

/// <summary>
/// Bepaalt welke afzenders een bouncemelding krijgen en verstuurt die meldingen.
/// Nieuwe afzenders staan standaard uit, zodat er nooit ongevraagd post uitgaat.
/// </summary>
public sealed class BounceNotificationService
{
    private readonly MailLogInspectorStore _store;
    private readonly BounceNotificationOperationalStore _notificationStore;
    private readonly Func<BounceNotificationSettings, IBounceMailTransport> _transportFactory;
    private readonly string _attachmentDirectory;

    public BounceNotificationService(
        MailLogInspectorStore store,
        BounceNotificationOperationalStore notificationStore,
        Func<BounceNotificationSettings, IBounceMailTransport> transportFactory,
        string attachmentDirectory)
    {
        _store = store;
        _notificationStore = notificationStore;
        _transportFactory = transportFactory;
        _attachmentDirectory = attachmentDirectory;
    }

    /// <summary>
    /// Bouwt het overzicht van afzenders met bounces voor een import. Onbekende afzenders worden
    /// als uitgeschakelde regel vastgelegd zodat ze in het instellingenscherm verschijnen.
    /// </summary>
    public IReadOnlyList<BounceNotificationPlanItem> BuildPlan(long importId)
    {
        return BuildPlan(_store.ReadSenderBounceReports(importId));
    }

    /// <summary>
    /// Bouwt het overzicht over een zelfgekozen periode, zodat een overgeslagen dag of week
    /// alsnog gemeld kan worden zonder opnieuw te importeren.
    /// </summary>
    public IReadOnlyList<BounceNotificationPlanItem> BuildPlan(BounceNotificationPeriod period)
    {
        IReadOnlyList<MailLogInspectorSenderBounceReport> reports =
            BounceNotificationScope.Normalize(period.Scope) == BounceNotificationScope.Range ||
            period.ImportId is null
                ? _store.ReadSenderBounceReports(period.FromInclusive, period.ThroughInclusive)
                : _store.ReadSenderBounceReports(period.ImportId.Value);

        return BuildPlan(reports);
    }

    private IReadOnlyList<BounceNotificationPlanItem> BuildPlan(
        IReadOnlyList<MailLogInspectorSenderBounceReport> reports)
    {
        if (reports.Count == 0)
        {
            return [];
        }

        _notificationStore.EnsureSendersExist(reports.Select(report => report.SenderAddress));

        Dictionary<string, BounceNotificationSender> settings = _notificationStore
            .LoadSenders()
            .ToDictionary(sender => sender.SenderAddress, StringComparer.OrdinalIgnoreCase);

        List<BounceNotificationPlanItem> items = [];
        foreach (MailLogInspectorSenderBounceReport report in reports)
        {
            if (!settings.TryGetValue(report.SenderAddress, out BounceNotificationSender? setting))
            {
                setting = BounceNotificationSender.CreateDisabled(report.SenderAddress);
            }

            items.Add(new BounceNotificationPlanItem(
                report,
                setting,
                MailLogInspectorNotificationAddressPolicy.SuggestRecipient(report.SenderAddress)));
        }

        return items;
    }

    /// <summary>Verstuurt de meldingen voor de opgegeven afzenders en legt elke poging vast in het logboek.</summary>
    public async Task<IReadOnlyList<BounceNotificationSendResult>> SendAsync(
        IReadOnlyList<BounceNotificationPlanItem> items,
        BounceNotificationPeriod period,
        CancellationToken cancellationToken)
    {
        BounceNotificationSettings settings = _notificationStore.LoadSettings();
        IBounceMailTransport transport = _transportFactory(settings);
        DateTime reportDate = period.ReportDate;
        string? sourceFileName = period.SourceFileName;

        List<BounceNotificationSendResult> results = [];
        foreach (BounceNotificationPlanItem item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!item.IsSendable)
            {
                continue;
            }

            string? attachmentPath = null;
            try
            {
                BounceNotificationContentOptions content = settings.ResolveContent();

                if (content.IncludeExcelAttachment)
                {
                    attachmentPath = BounceNotificationExcelWriter.Write(
                        _attachmentDirectory,
                        item.Report,
                        reportDate);
                }

                var message = new BounceNotificationMessage(
                    ToAddress: item.EffectiveRecipient,
                    Subject: BounceNotificationContentBuilder.BuildSubject(
                        settings.ResolveSubjectTemplate(),
                        item.Report,
                        reportDate),
                    HtmlBody: content.WantsHtml()
                        ? BounceNotificationContentBuilder.BuildHtmlBody(
                            item.Report,
                            reportDate,
                            sourceFileName,
                            hasAttachment: attachmentPath is not null,
                            content)
                        : null,
                    PlainTextBody: content.WantsPlainText()
                        ? BounceNotificationContentBuilder.BuildPlainTextBody(
                            item.Report,
                            reportDate,
                            sourceFileName,
                            hasAttachment: attachmentPath is not null,
                            content)
                        : null,
                    AttachmentPath: attachmentPath,
                    AttachmentFileName: attachmentPath is null
                        ? null
                        : BounceNotificationContentBuilder.BuildAttachmentFileName(item.Report, reportDate));

                await transport.SendAsync(message, cancellationToken);

                _notificationStore.RecordNotification(
                    item.Report.SenderAddress,
                    DateTime.UtcNow,
                    item.Report.BounceCount);

                _notificationStore.AppendLogEntry(
                    item.Report.SenderAddress,
                    item.EffectiveRecipient,
                    item.Report.BounceCount,
                    period,
                    success: true,
                    errorMessage: null);

                results.Add(new BounceNotificationSendResult(
                    item.Report.SenderAddress,
                    item.EffectiveRecipient,
                    Success: true,
                    ErrorMessage: null));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _notificationStore.AppendLogEntry(
                    item.Report.SenderAddress,
                    item.EffectiveRecipient,
                    item.Report.BounceCount,
                    period,
                    success: false,
                    errorMessage: ex.Message);

                results.Add(new BounceNotificationSendResult(
                    item.Report.SenderAddress,
                    item.EffectiveRecipient,
                    Success: false,
                    ErrorMessage: ex.Message));
            }
            finally
            {
                TryDeleteAttachment(attachmentPath);
            }
        }

        return results;
    }

    private static void TryDeleteAttachment(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Tijdelijk bestand blijft staan; dit mag het versturen niet blokkeren.
        }
        catch (UnauthorizedAccessException)
        {
            // Idem.
        }
    }
}
