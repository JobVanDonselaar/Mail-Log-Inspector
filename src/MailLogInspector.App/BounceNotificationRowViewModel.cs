using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using MailLogInspector.Core;
using MailLogInspector.Storage;

namespace MailLogInspector.App;

/// <summary>Bewerkbare regel in het overzicht van afzenders met bounces.</summary>
public sealed class BounceNotificationRowViewModel : INotifyPropertyChanged
{
    private bool _enabled;
    private string _recipient;
    private DateTime? _alreadySentAtUtc;

    public BounceNotificationRowViewModel(BounceNotificationPlanItem item)
    {
        Report = item.Report;
        SuggestedRecipient = item.SuggestedRecipient;
        _enabled = item.Setting.Enabled;
        _recipient = item.EffectiveRecipient;
        LastNotifiedAtUtc = item.Setting.LastNotifiedAtUtc;
    }

    public MailLogInspectorSenderBounceReport Report { get; }

    public string SuggestedRecipient { get; }

    public DateTime? LastNotifiedAtUtc { get; }

    public string SenderAddress => Report.SenderAddress;

    public int BounceCount => Report.BounceCount;

    public int TotalCount => Report.TotalCount;

    public string BouncePercentDisplay =>
        Report.BouncePercent.ToString("0.0", CultureInfo.InvariantCulture) + "%";

    public string TopReason => Report.ReasonBreakdown.Count > 0
        ? $"{Report.ReasonBreakdown[0].Reason} ({Report.ReasonBreakdown[0].Count})"
        : "-";

    public string LastNotifiedDisplay => LastNotifiedAtUtc.HasValue
        ? LastNotifiedAtUtc.Value.ToLocalTime().ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture)
        : "nooit";

    /// <summary>Wanneer deze afzender de gekozen periode al gemeld kreeg, of null.</summary>
    public DateTime? AlreadySentAtUtc
    {
        get => _alreadySentAtUtc;
        set
        {
            _alreadySentAtUtc = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PeriodStateDisplay));
        }
    }

    /// <summary>Toont of deze periode al gemaild is, zodat dubbel versturen opvalt.</summary>
    public string PeriodStateDisplay => AlreadySentAtUtc.HasValue
        ? "verstuurd " + AlreadySentAtUtc.Value.ToLocalTime().ToString("dd-MM HH:mm", CultureInfo.InvariantCulture)
        : "nog niet";

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            OnPropertyChanged();
        }
    }

    public string Recipient
    {
        get => _recipient;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (string.Equals(_recipient, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _recipient = normalized;
            OnPropertyChanged();
        }
    }

    public BounceNotificationSender ToSetting()
    {
        string? recipientOverride =
            string.IsNullOrWhiteSpace(Recipient) ||
            string.Equals(Recipient, SuggestedRecipient, StringComparison.OrdinalIgnoreCase)
                ? null
                : Recipient;

        return new BounceNotificationSender(
            SenderAddress,
            Enabled,
            recipientOverride,
            LastNotifiedAtUtc,
            LastNotifiedBounceCount: 0);
    }

    public BounceNotificationPlanItem ToPlanItem() =>
        new(Report, ToSetting(), SuggestedRecipient);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>Een import zoals die in de keuzelijst van de E-mail-tab staat.</summary>
public sealed record EmailImportListItem(
    long ImportId,
    string FileName,
    DateTime ImportedAt,
    DateTime? ReportStart,
    DateTime? ReportEnd,
    int BounceCount)
{
    public string Display
    {
        get
        {
            string period = ReportStart.HasValue || ReportEnd.HasValue
                ? BounceNotificationPeriod.ForImport(ImportId, ReportStart, ReportEnd, FileName).DescribePeriod()
                : ImportedAt.ToString("dd-MM-yyyy", CultureInfo.CurrentCulture);

            return $"{period} · {FileName} · {BounceCount} bounce(s)";
        }
    }

    public BounceNotificationPeriod ToPeriod() =>
        BounceNotificationPeriod.ForImport(ImportId, ReportStart, ReportEnd, FileName);
}
