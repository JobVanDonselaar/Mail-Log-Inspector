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
    private bool _neverNotify;
    private string _recipient;
    private DateTime? _alreadySentAtUtc;

    public BounceNotificationRowViewModel(BounceNotificationPlanItem item)
    {
        Report = item.Report;
        SuggestedRecipient = item.SuggestedRecipient;
        _enabled = item.Setting.Enabled;
        _neverNotify = item.Setting.NeverNotify;
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

    public double BouncePercent => Report.BouncePercent;

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
            if (_neverNotify)
            {
                value = false;
            }

            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            OnPropertyChanged();
        }
    }

    public bool NeverNotify
    {
        get => _neverNotify;
        set
        {
            if (_neverNotify == value)
            {
                return;
            }

            _neverNotify = value;
            if (_neverNotify)
            {
                Enabled = false;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(NotificationMode));
            OnPropertyChanged(nameof(NotificationModeSortOrder));
            OnPropertyChanged(nameof(IsNeverNotify));
        }
    }

    public string NotificationMode
    {
        get => NeverNotify ? "Nooit" : Enabled ? "Aan" : "Uit";
        set
        {
            switch (value?.Trim().ToLowerInvariant())
            {
                case "aan":
                    NeverNotify = false;
                    Enabled = true;
                    break;
                case "nooit":
                    NeverNotify = true;
                    break;
                default:
                    NeverNotify = false;
                    Enabled = false;
                    break;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(NotificationModeSortOrder));
            OnPropertyChanged(nameof(IsNeverNotify));
        }
    }

    public int NotificationModeSortOrder => NeverNotify ? 2 : Enabled ? 1 : 0;

    public bool IsNeverNotify => NeverNotify;

    /// <summary>
    /// Leeg laten betekent "gebruik het voorstel", niet "geen ontvanger". Het veld toont daarom
    /// het voorgestelde adres weer zodra het wordt leeggemaakt, zodat het scherm hetzelfde zegt
    /// als wat er bij verzenden gebeurt.
    /// </summary>
    public string Recipient
    {
        get => _recipient;
        set
        {
            string entered = value?.Trim() ?? string.Empty;
            string normalized = entered.Length == 0 ? SuggestedRecipient : entered;

            if (string.Equals(_recipient, normalized, StringComparison.Ordinal))
            {
                if (!string.Equals(entered, normalized, StringComparison.Ordinal))
                {
                    OnPropertyChanged();
                }

                return;
            }

            _recipient = normalized;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Laat het scherm de bewaarde waarde opnieuw ophalen. Nodig na het bewerken van een cel:
    /// wie het ontvangerveld leegmaakt krijgt het voorstel terug, en dat moet zichtbaar worden.
    /// </summary>
    public void RefreshRecipientDisplay() => OnPropertyChanged(nameof(Recipient));

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
            NeverNotify,
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
