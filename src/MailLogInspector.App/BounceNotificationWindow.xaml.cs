using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MailLogInspector.Core;
using MailLogInspector.Storage;

namespace MailLogInspector.App;

/// <summary>Bewerkbare regel in het overzicht van afzenders met bounces.</summary>
public sealed class BounceNotificationRowViewModel : INotifyPropertyChanged
{
    private bool _enabled;
    private string _recipient;

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

/// <summary>
/// Toont de afzenders met bounces uit een import en laat de gebruiker per afzender bepalen
/// of - en waarheen - er een melding gaat.
/// </summary>
public partial class BounceNotificationWindow : Window
{
    private readonly BounceNotificationOperationalStore _notificationStore;
    private readonly BounceNotificationService _service;
    private readonly ObservableCollection<BounceNotificationRowViewModel> _rows = [];
    private readonly DateTime _reportDate;
    private readonly string? _sourceFileName;
    private bool _isBusy;

    public BounceNotificationWindow(
        BounceNotificationOperationalStore notificationStore,
        BounceNotificationService service,
        IReadOnlyList<BounceNotificationPlanItem> plan,
        DateTime reportDate,
        string? sourceFileName)
    {
        InitializeComponent();

        _notificationStore = notificationStore;
        _service = service;
        _reportDate = reportDate;
        _sourceFileName = sourceFileName;

        foreach (BounceNotificationPlanItem item in plan)
        {
            _rows.Add(new BounceNotificationRowViewModel(item));
        }

        BounceSendersGrid.ItemsSource = _rows;
        LoadSettings();
        UpdateSummary();
    }

    private void LoadSettings()
    {
        BounceNotificationSettings settings = _notificationStore.LoadSettings();

        SelectTransport(settings.Transport);
        BounceFromAddressTextBox.Text = settings.FromAddress ?? string.Empty;
        BounceFromNameTextBox.Text = settings.FromDisplayName ?? "Mail Log Inspector";
        BounceSubjectTextBox.Text = settings.ResolveSubjectTemplate();
        BounceAutoSendCheckBox.IsChecked = settings.AutoSendAfterImport;
        BounceRelayHostTextBox.Text = settings.RelayHost ?? string.Empty;
        BounceRelayPortTextBox.Text = settings.RelayPort.ToString(CultureInfo.InvariantCulture);
        BounceRelayUsernameTextBox.Text = settings.RelayUsername ?? string.Empty;

        LoadContentOptions(settings.ResolveContent());

        UpdateRelayPanelVisibility();

        if (_sourceFileName is { Length: > 0 })
        {
            BounceIntroTextBlock.Text =
                $"Bounces uit '{_sourceFileName}' van {_reportDate:dd-MM-yyyy}. " +
                "Nieuwe afzenders staan standaard uit; zet aan wie een melding moet krijgen.";
        }
    }

    /// <summary>Vult de inhoudsopties in het formulier.</summary>
    private void LoadContentOptions(BounceNotificationContentOptions content)
    {
        BounceIncludeKpiCheckBox.IsChecked = content.IncludeKpiSummary;
        BounceIncludeReasonsCheckBox.IsChecked = content.IncludeReasonBreakdown;
        BounceIncludeDomainsCheckBox.IsChecked = content.IncludeRecipientDomainBreakdown;
        BounceIncludeDetailsCheckBox.IsChecked = content.IncludeDetailTable;
        BounceIncludeSourceCheckBox.IsChecked = content.IncludeSourceFileName;
        BounceIncludeAttachmentCheckBox.IsChecked = content.IncludeExcelAttachment;
        BounceMaxRowsTextBox.Text = content.ResolveMaxDetailRows().ToString(CultureInfo.InvariantCulture);
        BounceIntroTextBox.Text = content.IntroText ?? string.Empty;
        BounceFooterTextBox.Text = content.FooterText ?? string.Empty;

        SelectBodyFormat(content.ResolveBodyFormat());
        UpdateContentControlState();
    }

    private void SelectBodyFormat(string bodyFormat)
    {
        string normalized = BounceNotificationBodyFormat.Normalize(bodyFormat);
        foreach (object rawItem in BounceBodyFormatComboBox.Items)
        {
            if (rawItem is ComboBoxItem item &&
                string.Equals(item.Tag as string, normalized, StringComparison.OrdinalIgnoreCase))
            {
                BounceBodyFormatComboBox.SelectedItem = item;
                return;
            }
        }

        BounceBodyFormatComboBox.SelectedIndex = 0;
    }

    private string ReadSelectedBodyFormat()
    {
        return BounceBodyFormatComboBox.SelectedItem is ComboBoxItem item
            ? BounceNotificationBodyFormat.Normalize(item.Tag as string)
            : BounceNotificationBodyFormat.Default;
    }

    /// <summary>Leest de inhoudsopties uit het formulier.</summary>
    private BounceNotificationContentOptions BuildContentOptions()
    {
        if (!int.TryParse(
                BounceMaxRowsTextBox.Text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int maxRows) || maxRows <= 0)
        {
            maxRows = BounceNotificationContentOptions.DefaultMaxDetailRows;
        }

        return new BounceNotificationContentOptions(
            IncludeExcelAttachment: BounceIncludeAttachmentCheckBox.IsChecked == true,
            IncludeKpiSummary: BounceIncludeKpiCheckBox.IsChecked == true,
            IncludeReasonBreakdown: BounceIncludeReasonsCheckBox.IsChecked == true,
            IncludeRecipientDomainBreakdown: BounceIncludeDomainsCheckBox.IsChecked == true,
            IncludeDetailTable: BounceIncludeDetailsCheckBox.IsChecked == true,
            IncludeSourceFileName: BounceIncludeSourceCheckBox.IsChecked == true,
            MaxDetailRows: Math.Min(maxRows, BounceNotificationContentOptions.MaxDetailRowsLimit),
            BodyFormat: ReadSelectedBodyFormat(),
            IntroText: BounceIntroTextBox.Text.Trim(),
            FooterText: BounceFooterTextBox.Text.Trim()).EnsureNotEmpty();
    }

    /// <summary>Houdt de samenvatting en de afhankelijke velden in lijn met de gekozen opties.</summary>
    private void UpdateContentControlState()
    {
        bool includeDetails = BounceIncludeDetailsCheckBox.IsChecked == true;
        BounceMaxRowsTextBox.IsEnabled = includeDetails;
        BounceMaxRowsLabel.Opacity = includeDetails ? 1.0 : 0.5;

        List<string> parts = [];
        if (BounceIncludeKpiCheckBox.IsChecked == true)
        {
            parts.Add("kerncijfers");
        }

        if (BounceIncludeReasonsCheckBox.IsChecked == true)
        {
            parts.Add("oorzaken");
        }

        if (BounceIncludeDomainsCheckBox.IsChecked == true)
        {
            parts.Add("domeinen");
        }

        if (includeDetails)
        {
            parts.Add("details");
        }

        string blocks = parts.Count == 0 ? "geen blokken" : string.Join(", ", parts);
        string attachment = BounceIncludeAttachmentCheckBox.IsChecked == true
            ? "met Excel-bijlage"
            : "zonder bijlage";

        BounceContentSummaryTextBlock.Text =
            $"{blocks} · {attachment} · {BounceNotificationBodyFormat.Describe(ReadSelectedBodyFormat())}";
    }

    private void BounceContentOption_Changed(object sender, RoutedEventArgs e)
    {
        UpdateContentControlState();
    }

    private void BounceIncludeDetailsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateContentControlState();
    }

    private void BounceBodyFormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        UpdateContentControlState();
    }

    private void SelectTransport(string transport)    {
        string normalized = BounceNotificationTransport.Normalize(transport);
        foreach (object rawItem in BounceTransportComboBox.Items)
        {
            if (rawItem is ComboBoxItem item &&
                string.Equals(item.Tag as string, normalized, StringComparison.OrdinalIgnoreCase))
            {
                BounceTransportComboBox.SelectedItem = item;
                return;
            }
        }

        BounceTransportComboBox.SelectedIndex = 0;
    }

    private string ReadSelectedTransport()
    {
        return BounceTransportComboBox.SelectedItem is ComboBoxItem item
            ? BounceNotificationTransport.Normalize(item.Tag as string)
            : BounceNotificationTransport.Default;
    }

    private void UpdateRelayPanelVisibility()
    {
        bool needsRelay = ReadSelectedTransport() != BounceNotificationTransport.Gmail;
        BounceRelayPanel.Visibility = needsRelay ? Visibility.Visible : Visibility.Collapsed;
    }

    private BounceNotificationSettings BuildSettings()
    {
        BounceNotificationSettings existing = _notificationStore.LoadSettings();

        if (!int.TryParse(BounceRelayPortTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) ||
            port <= 0)
        {
            port = 587;
        }

        string? encryptedPassword = BounceRelayPasswordBox.Password.Length > 0
            ? GmailOAuthService.ProtectSecret(BounceRelayPasswordBox.Password)
            : existing.EncryptedRelayPassword;

        return existing with
        {
            Enabled = _rows.Any(row => row.Enabled),
            AutoSendAfterImport = BounceAutoSendCheckBox.IsChecked == true,
            Transport = ReadSelectedTransport(),
            FromAddress = BounceFromAddressTextBox.Text.Trim(),
            FromDisplayName = BounceFromNameTextBox.Text.Trim(),
            SubjectTemplate = BounceSubjectTextBox.Text.Trim(),
            RelayHost = BounceRelayHostTextBox.Text.Trim(),
            RelayPort = port,
            RelayUsername = BounceRelayUsernameTextBox.Text.Trim(),
            EncryptedRelayPassword = encryptedPassword,
            Content = BuildContentOptions()
        };
    }

    private void PersistAll()
    {
        BounceSendersGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
        _notificationStore.SaveSettings(BuildSettings());
        _notificationStore.SaveSenders(_rows.Select(row => row.ToSetting()));
    }

    private void UpdateSummary()
    {
        int enabled = _rows.Count(row => row.Enabled);
        int bounces = _rows.Where(row => row.Enabled).Sum(row => row.BounceCount);
        BounceSummaryTextBlock.Text =
            $"{_rows.Count} afzender(s) met bounces · {enabled} aangezet · {bounces} bounce(s) in de meldingen";
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _isBusy = busy;
        BounceProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        BounceSendButton.IsEnabled = !busy;
        BounceSaveButton.IsEnabled = !busy;
        BounceEnableAllButton.IsEnabled = !busy;
        BounceDisableAllButton.IsEnabled = !busy;

        if (status is not null)
        {
            BounceStatusTextBlock.Text = status;
        }
    }

    private void BounceTransportComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        UpdateRelayPanelVisibility();
    }

    private void BounceSendersGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(UpdateSummary));
    }

    private void BounceEnableAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (BounceNotificationRowViewModel row in _rows)
        {
            row.Enabled = true;
        }

        UpdateSummary();
    }

    private void BounceDisableAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (BounceNotificationRowViewModel row in _rows)
        {
            row.Enabled = false;
        }

        UpdateSummary();
    }

    private void BouncePreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: BounceNotificationRowViewModel row })
        {
            return;
        }

        try
        {
            BounceNotificationContentOptions content = BuildContentOptions();

            string subject = BounceNotificationContentBuilder.BuildSubject(
                string.IsNullOrWhiteSpace(BounceSubjectTextBox.Text)
                    ? BounceNotificationSettings.DefaultSubjectTemplate
                    : BounceSubjectTextBox.Text,
                row.Report,
                _reportDate);

            string body;
            string extension;
            if (content.WantsHtml())
            {
                body = BounceNotificationContentBuilder.BuildHtmlBody(
                    row.Report,
                    _reportDate,
                    _sourceFileName,
                    hasAttachment: content.IncludeExcelAttachment,
                    content);
                extension = "html";
            }
            else
            {
                body = BounceNotificationContentBuilder.BuildPlainTextBody(
                    row.Report,
                    _reportDate,
                    _sourceFileName,
                    hasAttachment: content.IncludeExcelAttachment,
                    content);
                extension = "txt";
            }

            string path = Path.Combine(
                Path.GetTempPath(),
                $"bounce-voorbeeld-{Guid.NewGuid():N}.{extension}");

            string header = extension == "html"
                ? $"<!-- Aan: {row.Recipient} | Onderwerp: {subject} -->{Environment.NewLine}"
                : $"Aan: {row.Recipient}{Environment.NewLine}Onderwerp: {subject}{Environment.NewLine}{new string('-', 60)}{Environment.NewLine}";

            File.WriteAllText(path, header + body);

            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = path;
            process.StartInfo.UseShellExecute = true;
            process.Start();

            BounceStatusTextBlock.Text = $"Voorbeeld geopend voor {row.SenderAddress}.";
        }
        catch (Exception ex)
        {
            BounceStatusTextBlock.Text = $"Voorbeeld mislukt: {ex.Message}";
        }
    }

    private void BounceSaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PersistAll();
            UpdateSummary();
            BounceStatusTextBlock.Text = "Instellingen opgeslagen.";
        }
        catch (Exception ex)
        {
            BounceStatusTextBlock.Text = $"Opslaan mislukt: {ex.Message}";
        }
    }

    private async void BounceSendButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        BounceSendersGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);

        List<BounceNotificationPlanItem> selected = _rows
            .Where(row => row.Enabled)
            .Select(row => row.ToPlanItem())
            .Where(item => item.IsSendable)
            .ToList();

        if (selected.Count == 0)
        {
            BounceStatusTextBlock.Text =
                "Geen verzendbare afzenders. Zet minstens één afzender aan en controleer het ontvangeradres.";
            return;
        }

        MessageBoxResult confirm = System.Windows.MessageBox.Show(
            this,
            $"{selected.Count} melding(en) versturen?",
            "Bouncemeldingen versturen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(true, $"Bezig met versturen van {selected.Count} melding(en)...");

        try
        {
            PersistAll();

            IReadOnlyList<BounceNotificationSendResult> results =
                await _service.SendAsync(selected, _reportDate, _sourceFileName, CancellationToken.None);

            int ok = results.Count(result => result.Success);
            int failed = results.Count - ok;

            string message = failed == 0
                ? $"{ok} melding(en) verstuurd."
                : $"{ok} verstuurd, {failed} mislukt. Eerste fout: " +
                  results.First(result => !result.Success).ErrorMessage;

            SetBusy(false, message);
        }
        catch (Exception ex)
        {
            SetBusy(false, $"Versturen mislukt: {ex.Message}");
        }
    }

    private void BounceCloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
