using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MailLogInspector.Core;
using MailLogInspector.Storage;

namespace MailLogInspector.App;

/// <summary>
/// De E-mail-tab: hier staat alles rond bouncemeldingen bij elkaar. Kies over welke import of
/// periode gemeld wordt, wie een melding krijgt, wat er in de mail komt en wat er al verstuurd is.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Waar de tab op openstaat. Gisteren is de eerste volledig geïmporteerde dag, zodat het
    /// overzicht meteen klopt en er niet eerst een import geladen wordt die daarna toch
    /// omgezet moet worden.
    /// </summary>
    private const string DefaultEmailScope = "yesterday";

    private readonly ObservableCollection<BounceNotificationRowViewModel> _emailRows = [];
    private CollectionViewSource? _emailRowsView;
    private IReadOnlyList<EmailImportListItem> _emailImports = [];
    private BounceNotificationPeriod? _emailPeriod;
    private bool _emailTabInitialized;
    private bool _emailBusy;
    private bool _emailSuppressEvents;

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

    /// <summary>Eenmalige opbouw zodra de tab voor het eerst geopend wordt.</summary>
    private void EmailTab_SelectionChanged()
    {
        if (_emailTabInitialized)
        {
            return;
        }

        _emailTabInitialized = true;

        try
        {
            _emailRowsView = new CollectionViewSource { Source = _emailRows };
            _emailRowsView.Filter += EmailRowsView_Filter;
            EmailSendersGrid.ItemsSource = _emailRowsView.View;
            LoadEmailSettingsIntoForm();
            ReloadEmailImportChoices();
            RefreshEmailHistory();

            if (_emailRows.Count == 0)
            {
                _ = LoadEmailOverviewAsync();
            }
        }
        catch (Exception ex)
        {
            MailLogInspectorLog.Error("bounce-notify", "Opbouwen van de E-mail-tab mislukt", ex);
            EmailStatusTextBlock.Text = "De E-mail-tab kon niet volledig worden opgebouwd: " + ex.Message;
        }
    }

    // ---------------------------------------------------------------- periode

    private void ReloadEmailImportChoices()
    {
        IReadOnlyList<MailLogInspectorImportedFile> imports = _store.ReadRecentImports(100);

        _emailImports = imports
            .Select(import => new EmailImportListItem(
                import.ImportId,
                import.SourceFileName,
                import.ImportedAt,
                import.ReportStart,
                import.ReportEnd,
                import.BounceCount))
            .ToList();

        _emailSuppressEvents = true;
        try
        {
            EmailImportComboBox.ItemsSource = _emailImports;
            if (_emailImports.Count > 0 && EmailImportComboBox.SelectedItem is null)
            {
                EmailImportComboBox.SelectedIndex = 0;
            }
        }
        finally
        {
            _emailSuppressEvents = false;
        }
    }

    private string ReadSelectedEmailScope()
    {
        return EmailScopeComboBox.SelectedItem is ComboBoxItem { Tag: string tag } ? tag : DefaultEmailScope;
    }

    /// <summary>Vertaalt de keuzes bovenin naar een concrete periode.</summary>
    private BounceNotificationPeriod? ResolveSelectedEmailPeriod(out string? error)
    {
        error = null;
        DateTime today = DateTime.Today;

        switch (ReadSelectedEmailScope())
        {
            case "latest":
            {
                EmailImportListItem? latest = _emailImports.Count > 0 ? _emailImports[0] : null;
                if (latest is null)
                {
                    error = "Er is nog geen import om over te melden.";
                    return null;
                }

                return latest.ToPeriod();
            }

            case "import":
            {
                if (EmailImportComboBox.SelectedItem is not EmailImportListItem chosen)
                {
                    error = "Kies eerst een import.";
                    return null;
                }

                return chosen.ToPeriod();
            }

            case "yesterday":
                return BounceNotificationPeriod.ForRange(today.AddDays(-1), today.AddDays(-1));

            case "last7":
                return BounceNotificationPeriod.ForRange(today.AddDays(-7), today.AddDays(-1));

            case "thismonth":
                return BounceNotificationPeriod.ForRange(new DateTime(today.Year, today.Month, 1), today);

            default:
            {
                DateTime? from = EmailFromDatePicker.SelectedDate;
                DateTime? through = EmailThroughDatePicker.SelectedDate;

                if (from is null || through is null)
                {
                    error = "Kies een begin- en einddatum.";
                    return null;
                }

                return BounceNotificationPeriod.ForRange(from.Value, through.Value);
            }
        }
    }

    private void EmailScopeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _emailSuppressEvents)
        {
            return;
        }

        UpdateEmailScopeControlState();
    }

    private void EmailImportComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _emailSuppressEvents)
        {
            return;
        }

        UpdateEmailScopeSummary();
    }

    private void UpdateEmailScopeControlState()
    {
        string scope = ReadSelectedEmailScope();
        bool picksImport = scope == "import";
        bool picksDates = scope == "custom";

        EmailImportPickerPanel.Visibility = picksImport ? Visibility.Visible : Visibility.Collapsed;
        EmailFromDatePanel.Visibility = picksDates ? Visibility.Visible : Visibility.Collapsed;
        EmailThroughDatePanel.Visibility = picksDates ? Visibility.Visible : Visibility.Collapsed;

        if (picksDates && EmailFromDatePicker.SelectedDate is null)
        {
            EmailThroughDatePicker.SelectedDate = DateTime.Today.AddDays(-1);
            EmailFromDatePicker.SelectedDate = DateTime.Today.AddDays(-1);
        }

        UpdateEmailScopeSummary();
    }

    private void UpdateEmailScopeSummary()
    {
        BounceNotificationPeriod? period = ResolveSelectedEmailPeriod(out string? error);

        EmailScopeSummaryTextBlock.Text = period is null
            ? error ?? "Kies waarover u wilt melden."
            : $"{period.Describe()} — druk op Overzicht ophalen om de afzenders te laden.";
    }

    private void EmailLoadButton_Click(object sender, RoutedEventArgs e)
    {
        _ = LoadEmailOverviewAsync();
    }

    /// <summary>Haalt de afzenders met bounces op voor de gekozen periode.</summary>
    private async Task LoadEmailOverviewAsync()
    {
        if (_emailBusy)
        {
            return;
        }

        BounceNotificationPeriod? period = ResolveSelectedEmailPeriod(out string? error);
        if (period is null)
        {
            EmailStatusTextBlock.Text = error ?? "Kies eerst een periode.";
            return;
        }

        SetEmailBusy(true, $"Bezig met ophalen: {period.Describe()}...");

        try
        {
            BounceNotificationService service = CreateBounceNotificationService();
            IReadOnlyList<BounceNotificationPlanItem> plan = await Task.Run(() => service.BuildPlan(period));
            IReadOnlyDictionary<string, DateTime> alreadySent = await Task.Run(() =>
                _bounceNotificationStore.ReadSuccessfulSendsForPeriod(period.FromInclusive, period.ThroughInclusive));

            _emailPeriod = period;
            ApplyEmailPlan(plan, alreadySent);

            SetEmailBusy(false, plan.Count == 0
                ? $"Geen bounces gevonden voor {period.Describe()}."
                : $"{plan.Count} afzender(s) met bounces voor {period.Describe()}.");
        }
        catch (Exception ex)
        {
            MailLogInspectorLog.Error("bounce-notify", $"Ophalen van bouncemeldingen mislukt ({period.Describe()})", ex);
            SetEmailBusy(false, "Ophalen mislukt: " + ex.Message);
        }
    }

    private void ApplyEmailPlan(
        IReadOnlyList<BounceNotificationPlanItem> plan,
        IReadOnlyDictionary<string, DateTime> alreadySent)
    {
        _emailRows.Clear();
        foreach (BounceNotificationPlanItem item in plan)
        {
            var row = new BounceNotificationRowViewModel(item);
            if (alreadySent.TryGetValue(row.SenderAddress, out DateTime sentAt))
            {
                row.AlreadySentAtUtc = sentAt;
            }

            _emailRows.Add(row);
        }

        UpdateEmailSummary();
    }

    // ------------------------------------------------------------- instellingen

    private void LoadEmailSettingsIntoForm()
    {
        BounceNotificationSettings settings = _bounceNotificationStore.LoadSettings();

        _emailSuppressEvents = true;
        try
        {
            SelectComboBoxByTag(EmailTransportComboBox, BounceNotificationTransport.Normalize(settings.Transport));
            EmailFromAddressTextBox.Text = settings.FromAddress ?? string.Empty;
            EmailFromNameTextBox.Text = settings.FromDisplayName ?? "Mail Log Inspector";
            EmailBccAddressTextBox.Text = settings.BccAddress ?? string.Empty;
            EmailSubjectTextBox.Text = settings.ResolveSubjectTemplate();
            EmailAutoSendCheckBox.IsChecked = settings.AutoSendAfterImport;
            EmailClearGmailSentCheckBox.IsChecked = settings.ClearGmailSentItemsAfterSend;
            EmailRelayHostTextBox.Text = settings.RelayHost ?? string.Empty;
            EmailRelayPortTextBox.Text = settings.RelayPort.ToString(CultureInfo.InvariantCulture);
            EmailRelayUsernameTextBox.Text = settings.RelayUsername ?? string.Empty;

            BounceNotificationContentOptions content = settings.ResolveContent();
            EmailIncludeKpiCheckBox.IsChecked = content.IncludeKpiSummary;
            EmailIncludeReasonsCheckBox.IsChecked = content.IncludeReasonBreakdown;
            EmailIncludeDomainsCheckBox.IsChecked = content.IncludeRecipientDomainBreakdown;
            EmailIncludeDetailsCheckBox.IsChecked = content.IncludeDetailTable;
            EmailIncludeSourceCheckBox.IsChecked = content.IncludeSourceFileName;
            EmailIncludeAttachmentCheckBox.IsChecked = content.IncludeExcelAttachment;
            EmailMaxRowsTextBox.Text = content.ResolveMaxDetailRows().ToString(CultureInfo.InvariantCulture);
            EmailIntroTextBox.Text = content.IntroText ?? string.Empty;
            EmailFooterTextBox.Text = content.FooterText ?? string.Empty;
            SelectComboBoxByTag(EmailBodyFormatComboBox, content.ResolveBodyFormat());

            if (EmailScopeComboBox.SelectedItem is null)
            {
                SelectComboBoxByTag(EmailScopeComboBox, DefaultEmailScope);
            }
        }
        finally
        {
            _emailSuppressEvents = false;
        }

        UpdateEmailRelayPanelVisibility();
        UpdateEmailContentControlState();
        UpdateEmailScopeControlState();
    }

    private static void SelectComboBoxByTag(System.Windows.Controls.ComboBox comboBox, string tag)
    {
        foreach (object rawItem in comboBox.Items)
        {
            if (rawItem is ComboBoxItem item &&
                string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }

    private string ReadSelectedEmailTransport()
    {
        return EmailTransportComboBox.SelectedItem is ComboBoxItem item
            ? BounceNotificationTransport.Normalize(item.Tag as string)
            : BounceNotificationTransport.Default;
    }

    private string ReadSelectedEmailBodyFormat()
    {
        return EmailBodyFormatComboBox.SelectedItem is ComboBoxItem item
            ? BounceNotificationBodyFormat.Normalize(item.Tag as string)
            : BounceNotificationBodyFormat.Default;
    }

    private void UpdateEmailRelayPanelVisibility()
    {
        string transport = ReadSelectedEmailTransport();
        bool needsRelay = transport != BounceNotificationTransport.Gmail;
        EmailRelayPanel.Visibility = needsRelay ? Visibility.Visible : Visibility.Collapsed;

        EmailTransportSummaryTextBlock.Text = transport switch
        {
            BounceNotificationTransport.SmtpRelay => "via SMTP-relay",
            BounceNotificationTransport.Microsoft365 => "via Microsoft 365",
            _ => "via Gmail"
        };
    }

    private BounceNotificationContentOptions BuildEmailContentOptions()
    {
        if (!int.TryParse(
                EmailMaxRowsTextBox.Text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int maxRows) || maxRows <= 0)
        {
            maxRows = BounceNotificationContentOptions.DefaultMaxDetailRows;
        }

        return new BounceNotificationContentOptions(
            IncludeExcelAttachment: EmailIncludeAttachmentCheckBox.IsChecked == true,
            IncludeKpiSummary: EmailIncludeKpiCheckBox.IsChecked == true,
            IncludeReasonBreakdown: EmailIncludeReasonsCheckBox.IsChecked == true,
            IncludeRecipientDomainBreakdown: EmailIncludeDomainsCheckBox.IsChecked == true,
            IncludeDetailTable: EmailIncludeDetailsCheckBox.IsChecked == true,
            IncludeSourceFileName: EmailIncludeSourceCheckBox.IsChecked == true,
            MaxDetailRows: Math.Min(maxRows, BounceNotificationContentOptions.MaxDetailRowsLimit),
            BodyFormat: ReadSelectedEmailBodyFormat(),
            IntroText: EmailIntroTextBox.Text.Trim(),
            FooterText: EmailFooterTextBox.Text.Trim()).EnsureNotEmpty();
    }

    private BounceNotificationSettings BuildEmailSettings()
    {
        BounceNotificationSettings existing = _bounceNotificationStore.LoadSettings();

        if (!int.TryParse(
                EmailRelayPortTextBox.Text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int port) || port <= 0)
        {
            port = 587;
        }

        string? encryptedPassword = EmailRelayPasswordBox.Password.Length > 0
            ? GmailOAuthService.ProtectSecret(EmailRelayPasswordBox.Password)
            : existing.EncryptedRelayPassword;

        return existing with
        {
            Enabled = _emailRows.Any(row => row.Enabled),
            AutoSendAfterImport = EmailAutoSendCheckBox.IsChecked == true,
            ClearGmailSentItemsAfterSend = EmailClearGmailSentCheckBox.IsChecked == true,
            Transport = ReadSelectedEmailTransport(),
            FromAddress = EmailFromAddressTextBox.Text.Trim(),
            FromDisplayName = EmailFromNameTextBox.Text.Trim(),
            BccAddress = EmailBccAddressTextBox.Text.Trim(),
            SubjectTemplate = EmailSubjectTextBox.Text.Trim(),
            RelayHost = EmailRelayHostTextBox.Text.Trim(),
            RelayPort = port,
            RelayUsername = EmailRelayUsernameTextBox.Text.Trim(),
            EncryptedRelayPassword = encryptedPassword,
            Content = BuildEmailContentOptions()
        };
    }

    private void UpdateEmailContentControlState()
    {
        bool includeDetails = EmailIncludeDetailsCheckBox.IsChecked == true;
        EmailMaxRowsTextBox.IsEnabled = includeDetails;
        EmailMaxRowsLabel.Opacity = includeDetails ? 1.0 : 0.5;

        List<string> parts = [];
        if (EmailIncludeKpiCheckBox.IsChecked == true)
        {
            parts.Add("kerncijfers");
        }

        if (EmailIncludeReasonsCheckBox.IsChecked == true)
        {
            parts.Add("oorzaken");
        }

        if (EmailIncludeDomainsCheckBox.IsChecked == true)
        {
            parts.Add("domeinen");
        }

        if (includeDetails)
        {
            parts.Add("details");
        }

        string blocks = parts.Count == 0 ? "geen blokken" : string.Join(", ", parts);
        string attachment = EmailIncludeAttachmentCheckBox.IsChecked == true
            ? "met Excel-bijlage"
            : "zonder bijlage";

        EmailContentSummaryTextBlock.Text =
            $"{blocks} · {attachment} · {BounceNotificationBodyFormat.Describe(ReadSelectedEmailBodyFormat())}";
    }

    private void EmailContentOption_Changed(object sender, RoutedEventArgs e)
    {
        UpdateEmailContentControlState();
    }

    private void EmailIncludeDetailsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateEmailContentControlState();
    }

    private void EmailBodyFormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _emailSuppressEvents)
        {
            return;
        }

        UpdateEmailContentControlState();
    }

    private void EmailResetTextsButton_Click(object sender, RoutedEventArgs e)
    {
        EmailIntroTextBox.Text = BounceNotificationContentOptions.DefaultIntroText;
        EmailFooterTextBox.Text = BounceNotificationContentOptions.DefaultFooterText;
        EmailStatusTextBlock.Text = "Standaardteksten teruggezet. Druk op Opslaan om ze te bewaren.";
    }

    private void EmailTransportComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _emailSuppressEvents)
        {
            return;
        }

        UpdateEmailRelayPanelVisibility();
    }

    // ------------------------------------------------------------------ acties

    private void PersistEmailSettings()
    {
        EmailSendersGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
        _bounceNotificationStore.SaveSettings(BuildEmailSettings());
        _bounceNotificationStore.SaveSenders(_emailRows.Select(row => row.ToSetting()));
    }

    private void UpdateEmailSummary()
    {
        int enabled = _emailRows.Count(row => row.Enabled);
        int never = _emailRows.Count(row => row.NeverNotify);
        int bounces = _emailRows.Where(row => row.Enabled).Sum(row => row.BounceCount);
        int alreadySent = _emailRows.Count(row => row.Enabled && row.AlreadySentAtUtc.HasValue);

        string warning = alreadySent > 0
            ? $" · let op: {alreadySent} kreeg deze periode al een melding"
            : string.Empty;

        string blocked = never > 0
            ? $" · {never} op nooit"
            : string.Empty;

        EmailSendersHintTextBlock.Text =
            $"{_emailRows.Count} afzender(s) · {enabled} aangezet{blocked} · {bounces} bounce(s) in de meldingen{warning}";

        if (EmailTopStatusTextBlock != null)
        {
            EmailTopStatusTextBlock.Text = _emailPeriod is null
                ? "Bouncemeldingen naar afzenders."
                : $"{_emailPeriod.Describe()} · {enabled} van {_emailRows.Count} afzender(s) aangezet";
        }
    }

    private void SetEmailBusy(bool busy, string? status = null)
    {
        _emailBusy = busy;
        EmailProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        EmailSendButton.IsEnabled = !busy;
        EmailSaveButton.IsEnabled = !busy;
        EmailLoadButton.IsEnabled = !busy;
        EmailEnableAllButton.IsEnabled = !busy;
        EmailDisableAllButton.IsEnabled = !busy;

        if (status is not null)
        {
            EmailStatusTextBlock.Text = status;
        }
    }

    private void EmailSendersGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        var edited = e.Row?.Item as BounceNotificationRowViewModel;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            edited?.RefreshRecipientDisplay();
            UpdateEmailSummary();
        }));
    }

    /// <summary>
    /// Dubbelklikken op een afzender opent Zoeken met dat adres en de gekozen periode. Zo zijn de
    /// bounces achter het getal meteen te bekijken. Andere kolommen blijven bewerkbaar, dus alleen
    /// een klik in de afzenderkolom springt weg.
    /// </summary>
    private async void EmailSendersGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        System.Windows.Controls.DataGridCell? cell =
            FindVisualParent<System.Windows.Controls.DataGridCell>(e.OriginalSource as DependencyObject);
        if (cell is null ||
            !string.Equals(
                cell.Column?.SortMemberPath,
                nameof(BounceNotificationRowViewModel.SenderAddress),
                StringComparison.Ordinal))
        {
            return;
        }

        if (cell.DataContext is not BounceNotificationRowViewModel row)
        {
            return;
        }

        e.Handled = true;
        await OpenSenderInSearchAsync(row.SenderAddress);
    }

    /// <summary>Vult de zoekfilters met dit afzenderadres en de periode, en zoekt meteen.</summary>
    private async Task OpenSenderInSearchAsync(string senderAddress)
    {
        string address = senderAddress?.Trim() ?? string.Empty;
        if (address.Length == 0)
        {
            return;
        }

        if (_emailPeriod is not null)
        {
            SearchFromDatePicker.SelectedDate = _emailPeriod.FromInclusive.Date;
            SearchThroughDatePicker.SelectedDate = _emailPeriod.ThroughInclusive.Date;
        }

        SenderTextBox.Text = address;
        RecipientTextBox.Text = string.Empty;
        SelectSearchStatusFilter(null);
        SearchRunStateTextBlock.Text = "Afzender overgenomen uit de E-mail-tab: " + address;
        SearchRunDetailTextBlock.Text = "Bezig met zoeken...";

        MainTabControl.SelectedItem = SearchTab;

        await RunSearchAsync(SearchRunReason.FreshSearch);
    }

    /// <summary>Zoekt het dichtstbijzijnde bovenliggende element van het gevraagde type.</summary>
    private static T? FindVisualParent<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }

            source = source is System.Windows.Media.Visual
                ? System.Windows.Media.VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return null;
    }

    private void EmailEnableAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (BounceNotificationRowViewModel row in _emailRows)
        {
            row.Enabled = true;
        }

        UpdateEmailSummary();
    }

    private void EmailDisableAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (BounceNotificationRowViewModel row in _emailRows)
        {
            if (row.NeverNotify)
            {
                continue;
            }

            row.Enabled = false;
        }

        UpdateEmailSummary();
    }

    private void EmailHistoryRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshEmailHistory();
    }

    private void RefreshEmailHistory()
    {
        try
        {
            IReadOnlyList<BounceNotificationLogEntry> entries = _bounceNotificationStore.ReadLogEntries(250);
            EmailHistoryGrid.ItemsSource = entries;
            EmailHistoryHintTextBlock.Text = entries.Count == 0
                ? "Er is nog niets verstuurd."
                : $"{entries.Count} verzendpoging(en), nieuwste eerst.";
        }
        catch (Exception ex)
        {
            MailLogInspectorLog.Error("bounce-notify", "Lezen van de verzendgeschiedenis mislukt", ex);
            EmailHistoryHintTextBlock.Text = "Geschiedenis kon niet worden geladen: " + ex.Message;
        }
    }

    private void EmailPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: BounceNotificationRowViewModel row })
        {
            return;
        }

        try
        {
            BounceNotificationContentOptions content = BuildEmailContentOptions();
            DateTime reportDate = _emailPeriod?.ReportDate ?? DateTime.Today;
            string? sourceFileName = _emailPeriod?.SourceFileName;

            string subject = BounceNotificationContentBuilder.BuildSubject(
                string.IsNullOrWhiteSpace(EmailSubjectTextBox.Text)
                    ? BounceNotificationSettings.DefaultSubjectTemplate
                    : EmailSubjectTextBox.Text,
                row.Report,
                reportDate);

            string body;
            string extension;
            if (content.WantsHtml())
            {
                body = BounceNotificationContentBuilder.BuildHtmlBody(
                    row.Report,
                    reportDate,
                    sourceFileName,
                    hasAttachment: content.IncludeExcelAttachment,
                    content,
                    _emailPeriod?.FromInclusive ?? reportDate,
                    _emailPeriod?.ThroughInclusive ?? reportDate,
                    BounceNotificationHeaderLogo.DataUri);
                extension = "html";
            }
            else
            {
                body = BounceNotificationContentBuilder.BuildPlainTextBody(
                    row.Report,
                    reportDate,
                    sourceFileName,
                    hasAttachment: content.IncludeExcelAttachment,
                    content,
                    _emailPeriod?.FromInclusive ?? reportDate,
                    _emailPeriod?.ThroughInclusive ?? reportDate);
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

            EmailStatusTextBlock.Text = $"Voorbeeld geopend voor {row.SenderAddress}.";
        }
        catch (Exception ex)
        {
            EmailStatusTextBlock.Text = "Voorbeeld mislukt: " + ex.Message;
        }
    }

    private void EmailSaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PersistEmailSettings();
            UpdateEmailSummary();
            EmailStatusTextBlock.Text = "Instellingen opgeslagen.";
        }
        catch (Exception ex)
        {
            MailLogInspectorLog.Error("bounce-notify", "Opslaan van de bounce-instellingen mislukt", ex);
            EmailStatusTextBlock.Text = "Opslaan mislukt: " + ex.Message;
        }
    }

    private async void EmailSendButton_Click(object sender, RoutedEventArgs e)
    {
        if (_emailBusy)
        {
            return;
        }

        if (_emailPeriod is null)
        {
            EmailStatusTextBlock.Text = "Haal eerst een overzicht op.";
            return;
        }

        EmailSendersGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);

        List<BounceNotificationPlanItem> selected = _emailRows
            .Where(row => row.Enabled)
            .Select(row => row.ToPlanItem())
            .Where(item => item.IsSendable)
            .ToList();

        if (selected.Count == 0)
        {
            EmailStatusTextBlock.Text =
                "Geen verzendbare afzenders. Zet minstens één afzender aan en controleer het ontvangeradres.";
            return;
        }

        int repeats = _emailRows.Count(row => row.Enabled && row.AlreadySentAtUtc.HasValue);
        string question = repeats > 0
            ? $"{selected.Count} melding(en) versturen voor {_emailPeriod.Describe()}?\n\n" +
              $"Let op: {repeats} afzender(s) kregen deze periode al een melding."
            : $"{selected.Count} melding(en) versturen voor {_emailPeriod.Describe()}?";

        MessageBoxResult confirm = System.Windows.MessageBox.Show(
            this,
            question,
            "Bouncemeldingen versturen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        SetEmailBusy(true, $"Bezig met versturen van {selected.Count} melding(en)...");

        try
        {
            PersistEmailSettings();

            BounceNotificationService service = CreateBounceNotificationService();
            IReadOnlyList<BounceNotificationSendResult> results =
                await service.SendAsync(selected, _emailPeriod, CancellationToken.None);

            int ok = results.Count(result => result.Success);
            int failed = results.Count - ok;

            MailLogInspectorLog.Info(
                "bounce-notify",
                $"{_emailPeriod.Describe()} | Verstuurd={ok} | Mislukt={failed}");

            SetEmailBusy(false, failed == 0
                ? $"{ok} melding(en) verstuurd."
                : $"{ok} verstuurd, {failed} mislukt. Eerste fout: " +
                  results.First(result => !result.Success).ErrorMessage);

            RefreshEmailHistory();
            MarkEmailRowsAsSent(results);
        }
        catch (Exception ex)
        {
            MailLogInspectorLog.Error("bounce-notify", "Versturen van bouncemeldingen mislukt", ex);
            SetEmailBusy(false, "Versturen mislukt: " + ex.Message);
        }
    }

    private void MarkEmailRowsAsSent(IReadOnlyList<BounceNotificationSendResult> results)
    {
        DateTime now = DateTime.UtcNow;
        foreach (BounceNotificationSendResult result in results.Where(result => result.Success))
        {
            BounceNotificationRowViewModel? row = _emailRows.FirstOrDefault(candidate =>
                string.Equals(candidate.SenderAddress, result.SenderAddress, StringComparison.OrdinalIgnoreCase));

            if (row is not null)
            {
                row.AlreadySentAtUtc = now;
            }
        }

        UpdateEmailSummary();
    }

    // -------------------------------------------------------------- na import

    /// <summary>
    /// Na een import staat het overzicht klaar in de E-mail-tab, of gaat het meteen de deur uit
    /// als automatisch versturen aanstaat. Fouten mogen de import nooit laten mislukken.
    /// </summary>
    private async Task RunBounceNotificationsAfterImportAsync(long importId, string? sourceFileName)
    {
        if (importId <= 0 || _activeArchiveMonthKey != null)
        {
            return;
        }

        try
        {
            MailLogInspectorImportedFile? import = _store
                .ReadRecentImports(20)
                .FirstOrDefault(candidate => candidate.ImportId == importId);

            BounceNotificationPeriod period = BounceNotificationPeriod.ForImport(
                importId,
                import?.ReportStart,
                import?.ReportEnd,
                sourceFileName ?? import?.SourceFileName);

            BounceNotificationService service = CreateBounceNotificationService();
            IReadOnlyList<BounceNotificationPlanItem> plan = await Task.Run(() => service.BuildPlan(period));

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
                await SendBounceNotificationsAutomaticallyAsync(service, plan, period);
                return;
            }

            ShowEmailTabWithPlan(plan, period);
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
        BounceNotificationPeriod period)
    {
        List<BounceNotificationPlanItem> sendable = plan.Where(item => item.IsSendable).ToList();
        if (sendable.Count == 0)
        {
            MailLogInspectorLog.Info(
                "bounce-notify",
                "Automatisch versturen aan, maar geen enkele afzender staat aan");
            return;
        }

        IReadOnlyList<BounceNotificationSendResult> results =
            await service.SendAsync(sendable, period, CancellationToken.None);

        int ok = results.Count(result => result.Success);
        int failed = results.Count - ok;

        MailLogInspectorLog.Info(
            "bounce-notify",
            $"Automatisch verstuurd | Geslaagd={ok} | Mislukt={failed}");

        StatusTextBlock.Text = failed == 0
            ? $"{ok} bouncemelding(en) automatisch verstuurd."
            : $"{ok} bouncemelding(en) verstuurd, {failed} mislukt. Zie het logboek.";

        if (_emailTabInitialized)
        {
            RefreshEmailHistory();
        }
    }

    /// <summary>Toont het klaargezette overzicht in de E-mail-tab en brengt die tab naar voren.</summary>
    private void ShowEmailTabWithPlan(
        IReadOnlyList<BounceNotificationPlanItem> plan,
        BounceNotificationPeriod period)
    {
        if (!_emailTabInitialized)
        {
            _emailTabInitialized = true;
            _emailRowsView = new CollectionViewSource { Source = _emailRows };
            _emailRowsView.Filter += EmailRowsView_Filter;
            EmailSendersGrid.ItemsSource = _emailRowsView.View;
            LoadEmailSettingsIntoForm();
        }

        ReloadEmailImportChoices();

        _emailSuppressEvents = true;
        try
        {
            SelectComboBoxByTag(EmailScopeComboBox, "latest");
        }
        finally
        {
            _emailSuppressEvents = false;
        }

        UpdateEmailScopeControlState();

        _emailPeriod = period;
        ApplyEmailPlan(
            plan,
            _bounceNotificationStore.ReadSuccessfulSendsForPeriod(period.FromInclusive, period.ThroughInclusive));
        RefreshEmailHistory();

        EmailStatusTextBlock.Text =
            $"{plan.Count} afzender(s) met bounces uit {period.Describe()}. Controleer de selectie en verstuur.";

        MainTabControl.SelectedItem = EmailTab;
        StatusTextBlock.Text = $"{plan.Count} afzender(s) met bounces. Zie de tab E-mail.";
    }

    // ── Afzenderfilter ──────────────────────────────────────────────────────────

    private void EmailRowsView_Filter(object sender, FilterEventArgs e)
    {
        string filter = EmailSenderFilterTextBox?.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(filter))
        {
            e.Accepted = true;
            return;
        }

        e.Accepted = e.Item is BounceNotificationRowViewModel row
            && row.SenderAddress.Contains(filter, System.StringComparison.OrdinalIgnoreCase);
    }

    private void EmailSenderFilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        EmailSenderFilterClearButton.Visibility =
            string.IsNullOrEmpty(EmailSenderFilterTextBox.Text)
                ? System.Windows.Visibility.Collapsed
                : System.Windows.Visibility.Visible;

        _emailRowsView?.View.Refresh();
    }

    private void EmailSenderFilterClearButton_Click(object sender, RoutedEventArgs e)
    {
        EmailSenderFilterTextBox.Clear();
        EmailSenderFilterTextBox.Focus();
    }
}
