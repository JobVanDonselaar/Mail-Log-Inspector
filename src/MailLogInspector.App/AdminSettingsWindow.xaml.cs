using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MailLogInspector.Core;
using MailLogInspector.Storage;

namespace MailLogInspector.App;

public partial class AdminSettingsWindow : Window
{
    private readonly GmailReportOperationalStore _store;
    private readonly SmtpPortalOperationalStore _smtpPortalStore;
    private readonly ReportSyncOperationalStore _syncStore;
    private readonly SmtpApiOperationalStore _smtpApiStore;
    private readonly AdminReportSyncRunner _reportSyncRunner;
    private readonly MailLogInspectorWorkspacePaths _workspace;
    private readonly GmailOAuthService _oauthService = new(new HttpClient());
    private readonly IGmailImapReportClient _mailClient = new GmailImapReportClient();
    private readonly ISmtpApiClient _smtpApiClient = new SmtpApiClient(new HttpClient());
    private GmailReportConfig _storedConfig;
    private SmtpPortalConfig _storedPortalConfig;
    private ReportSyncConfig _storedSyncConfig;
    private SmtpApiConfig _storedApiConfig;
    private CancellationTokenSource? _reportSyncCancellation;
    private string? _pendingRefreshToken;
    private string? _connectionStatus;

    public AdminSettingsWindow(
        GmailReportOperationalStore store,
        SmtpPortalOperationalStore smtpPortalStore,
        ReportSyncOperationalStore syncStore,
        SmtpApiOperationalStore smtpApiStore,
        MailLogInspectorWorkspacePaths workspace)
    {
        InitializeComponent();
        _store = store;
        _smtpPortalStore = smtpPortalStore;
        _syncStore = syncStore;
        _smtpApiStore = smtpApiStore;
        _workspace = workspace;
        _reportSyncRunner = new AdminReportSyncRunner(workspace, store, smtpPortalStore, syncStore, smtpApiStore);
        _storedConfig = store.LoadConfig();
        _storedPortalConfig = smtpPortalStore.LoadConfig();
        _storedSyncConfig = syncStore.LoadConfig();
        _storedApiConfig = smtpApiStore.LoadConfig();
        Closed += (_, _) => _reportSyncCancellation?.Cancel();
        LoadConfig();
    }

    private void LoadConfig()
    {
        SelectSyncMode(_storedSyncConfig.Mode);
        SelectImapProvider(_storedConfig.ImapProvider);
        SelectAuthenticationMode(_storedConfig.AuthenticationMode);
        AdminGmailAccountTextBox.Text = _storedConfig.AccountEmailAddress ?? string.Empty;
        AdminGmailClientIdTextBox.Text = _storedConfig.ClientId ?? string.Empty;
        AdminCustomImapHostTextBox.Text = _storedConfig.ImapHost ?? string.Empty;
        AdminCustomImapPortTextBox.Text = _storedConfig.ImapPort.ToString();
        AdminCustomImapUseSslCheckBox.IsChecked = _storedConfig.ImapUseSsl;
        AdminGmailAppPasswordBox.Password = string.IsNullOrWhiteSpace(_storedConfig.EncryptedAppPassword)
            ? string.Empty
            : GmailAdminConfigBuilder.StoredSecretPlaceholder;
        AdminAutoSyncCheckBox.IsChecked = _storedSyncConfig.AutoSyncEnabled;
        AdminCloseToTrayCheckBox.IsChecked = _storedSyncConfig.CloseToTrayEnabled;
        AdminConnectionStatusTextBlock.Text = string.IsNullOrWhiteSpace(_storedConfig.ConnectionStatus)
            ? "Wijzig de IMAP-instellingen of test de verbinding."
            : "Huidige IMAP-status: " + _storedConfig.ConnectionStatus;

        AdminSmtpPortalUsernameTextBox.Text = _storedPortalConfig.Username ?? string.Empty;
        AdminSmtpPortalPasswordBox.Password =
            string.IsNullOrWhiteSpace(_storedPortalConfig.EncryptedPassword)
                ? string.Empty
                : SmtpPortalAdminConfigBuilder.StoredSecretPlaceholder;
        AdminSmtpPortalTotpSecretBox.Password =
            string.IsNullOrWhiteSpace(_storedPortalConfig.EncryptedTotpSecret)
                ? string.Empty
                : SmtpPortalAdminConfigBuilder.StoredSecretPlaceholder;
        AdminSmtpPortalStatusTextBlock.Text = string.IsNullOrWhiteSpace(_storedPortalConfig.ConnectionStatus)
            ? "Nog geen SMTP.com-proefdownload uitgevoerd."
            : _storedPortalConfig.ConnectionStatus;
        AdminDefaultReportSyntaxTextBox.Text = SmtpPortalReportNameSyntax.DefaultTemplate;
        AdminCustomReportSyntaxTextBox.Text = _storedPortalConfig.CustomReportSyntax ?? string.Empty;
        AdminDefaultReportSyntaxRadioButton.IsChecked = _storedPortalConfig.UseDefaultReportSyntax;
        AdminCustomReportSyntaxRadioButton.IsChecked = !_storedPortalConfig.UseDefaultReportSyntax;
        AdminSmtpPortalLastUsedTextBlock.Text = _storedPortalConfig.LastSuccessfulPortalUseAtUtc.HasValue
            ? "Portaalsessie laatst succesvol gebruikt: " +
              _storedPortalConfig.LastSuccessfulPortalUseAtUtc.Value.ToLocalTime().ToString("dd-MM-yyyy HH:mm")
            : "Portaalsessie nog niet succesvol gebruikt.";
        UpdateReportSyntaxUi();
        LoadApiConfig();
        UpdatePortalSecretSavedStatus();
        UpdateImapSecretSavedStatus();
        UpdateAuthenticationPanels();
    }

    private void LoadApiConfig()
    {
        AdminSmtpApiKeyBox.Password = _storedApiConfig.HasApiKey
            ? SmtpApiAdminConfigBuilder.StoredSecretPlaceholder
            : string.Empty;
        AdminSmtpApiKeySavedTextBlock.Text = _storedApiConfig.HasApiKey
            ? "******** versleuteld opgeslagen."
            : "Nog niet opgeslagen.";
        AdminSmtpApiChannelComboBox.Text = _storedApiConfig.Channel ?? string.Empty;
        AdminSmtpApiSyntax1TextBox.Text =
            _storedApiConfig.ReportSyntax1 ?? SmtpPortalReportNameSyntax.DefaultTemplate;
        AdminSmtpApiSyntax2TextBox.Text = _storedApiConfig.ReportSyntax2 ?? string.Empty;
        AdminSmtpApiSyntax3TextBox.Text = _storedApiConfig.ReportSyntax3 ?? string.Empty;
        AdminSmtpApiStatusTextBlock.Text = string.IsNullOrWhiteSpace(_storedApiConfig.ConnectionStatus)
            ? "Nog geen API-verbinding getest."
            : _storedApiConfig.ConnectionStatus;
        UpdateApiLastUsedStatus();
        UpdateApiSyntaxUi();
    }

    private void UpdatePortalSecretSavedStatus()
    {
        bool passwordStored = !string.IsNullOrWhiteSpace(_storedPortalConfig.EncryptedPassword);
        bool totpStored = !string.IsNullOrWhiteSpace(_storedPortalConfig.EncryptedTotpSecret);
        if (passwordStored && string.IsNullOrEmpty(AdminSmtpPortalPasswordBox.Password))
        {
            AdminSmtpPortalPasswordBox.Password = SmtpPortalAdminConfigBuilder.StoredSecretPlaceholder;
        }
        if (totpStored && string.IsNullOrEmpty(AdminSmtpPortalTotpSecretBox.Password))
        {
            AdminSmtpPortalTotpSecretBox.Password = SmtpPortalAdminConfigBuilder.StoredSecretPlaceholder;
        }

        AdminSmtpPortalPasswordSavedTextBlock.Text = passwordStored
            ? "******** versleuteld opgeslagen."
            : "Nog niet opgeslagen.";
        AdminSmtpPortalTotpSavedTextBlock.Text = totpStored
            ? "******** versleuteld opgeslagen."
            : "Nog niet opgeslagen.";
    }

    private void UpdateImapSecretSavedStatus()
    {
        bool passwordStored = !string.IsNullOrWhiteSpace(_storedConfig.EncryptedAppPassword);
        if (passwordStored && string.IsNullOrEmpty(AdminGmailAppPasswordBox.Password))
        {
            AdminGmailAppPasswordBox.Password = GmailAdminConfigBuilder.StoredSecretPlaceholder;
        }

        AdminGmailAppPasswordSavedTextBlock.Text = passwordStored
            ? "******** versleuteld opgeslagen."
            : "Nog niet opgeslagen.";
    }

    private string SelectedSyncMode =>
        ReportSyncMode.Normalize(
            (AdminSyncSourceComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString());

    private void SelectSyncMode(string? mode)
    {
        string normalized = ReportSyncMode.Normalize(mode);
        foreach (ComboBoxItem item in AdminSyncSourceComboBox.Items)
        {
            if (string.Equals(item.Tag?.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                AdminSyncSourceComboBox.SelectedItem = item;
                return;
            }
        }

        AdminSyncSourceComboBox.SelectedIndex = 0;
    }

    private string SelectedImapProvider => ImapProvider.Normalize(
        (AdminImapProviderComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString());

    private void SelectImapProvider(string? provider)
    {
        string normalized = ImapProvider.Normalize(provider);
        foreach (ComboBoxItem item in AdminImapProviderComboBox.Items)
        {
            if (string.Equals(item.Tag?.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                AdminImapProviderComboBox.SelectedItem = item;
                return;
            }
        }

        AdminImapProviderComboBox.SelectedIndex = 0;
    }

    private string SelectedAuthenticationMode =>
        (AdminGmailAuthModeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() is string mode &&
        string.Equals(mode, GmailAuthenticationMode.AppPassword, StringComparison.OrdinalIgnoreCase)
            ? GmailAuthenticationMode.AppPassword
            : GmailAuthenticationMode.OAuth;

    private void SelectAuthenticationMode(string? authenticationMode)
    {
        string normalized = string.Equals(authenticationMode, GmailAuthenticationMode.AppPassword, StringComparison.OrdinalIgnoreCase)
            ? GmailAuthenticationMode.AppPassword
            : GmailAuthenticationMode.OAuth;
        foreach (ComboBoxItem item in AdminGmailAuthModeComboBox.Items)
        {
            if (string.Equals(item.Tag?.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                AdminGmailAuthModeComboBox.SelectedItem = item;
                return;
            }
        }
        AdminGmailAuthModeComboBox.SelectedIndex = 1;
    }

    private GmailAdminSettingsInput ReadInput()
    {
        int? customPort = int.TryParse(AdminCustomImapPortTextBox.Text, out int parsedPort) &&
                          parsedPort is > 0 and <= 65535
            ? parsedPort
            : null;
        return new GmailAdminSettingsInput(
            AdminGmailAccountTextBox.Text,
            SelectedAuthenticationMode,
            AdminGmailClientIdTextBox.Text,
            AdminGmailClientSecretBox.Password,
            AdminGmailAppPasswordBox.Password,
            SelectedImapProvider,
            AdminCustomImapHostTextBox.Text,
            customPort,
            AdminCustomImapUseSslCheckBox.IsChecked == true);
    }

    private AdminSyncSettingsInput ReadSyncInput()
    {
        return new AdminSyncSettingsInput(
            SelectedSyncMode,
            AdminAutoSyncCheckBox.IsChecked == true,
            AdminCloseToTrayCheckBox.IsChecked == true);
    }

    private SmtpPortalAdminSettingsInput ReadPortalInput()
    {
        return new SmtpPortalAdminSettingsInput(
            AdminSmtpPortalUsernameTextBox.Text,
            AdminSmtpPortalPasswordBox.Password,
            AdminSmtpPortalTotpSecretBox.Password,
            AdminDefaultReportSyntaxRadioButton.IsChecked == true,
            AdminCustomReportSyntaxTextBox.Text);
    }

    private void AdminReportSyntaxMode_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            UpdateReportSyntaxUi();
        }
    }

    private void AdminCustomReportSyntaxTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (IsLoaded)
        {
            UpdateReportSyntaxUi();
        }
    }

    private void UpdateReportSyntaxUi()
    {
        bool useDefault = AdminDefaultReportSyntaxRadioButton.IsChecked == true;
        AdminDefaultReportSyntaxTextBox.Visibility = useDefault
            ? Visibility.Visible
            : Visibility.Collapsed;
        AdminCustomReportSyntaxTextBox.Visibility = useDefault
            ? Visibility.Collapsed
            : Visibility.Visible;

        string template = useDefault
            ? SmtpPortalReportNameSyntax.DefaultTemplate
            : AdminCustomReportSyntaxTextBox.Text;
        SmtpPortalReportSyntaxValidation validation =
            SmtpPortalReportNameSyntax.Validate(template);
        AdminReportSyntaxValidationTextBlock.Text = validation.ErrorMessage ?? string.Empty;
        AdminReportSyntaxPreviewTextBlock.Text = validation.IsValid
            ? "Voorbeeld: " + SmtpPortalReportNameSyntax.BuildExample(template)
            : "Voorbeeld is beschikbaar zodra de syntax geldig is.";
    }

    private bool TryBuildPortalConfig(out SmtpPortalConfig portalConfig)
    {
        try
        {
            portalConfig = SmtpPortalAdminConfigBuilder.Build(
                _smtpPortalStore.LoadConfig(),
                ReadPortalInput());
            AdminReportSyntaxValidationTextBlock.Text = string.Empty;
            return true;
        }
        catch (InvalidOperationException ex)
        {
            portalConfig = _smtpPortalStore.LoadConfig();
            AdminReportSyntaxValidationTextBlock.Text = ex.Message;
            AdminSmtpPortalStatusTextBlock.Text = "Rapportsyntax is ongeldig: " + ex.Message;
            return false;
        }
    }

    private void AdminSmtpApiSecretBox_GotKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        if (sender is PasswordBox passwordBox &&
            string.Equals(
                passwordBox.Password,
                SmtpApiAdminConfigBuilder.StoredSecretPlaceholder,
                StringComparison.Ordinal))
        {
            passwordBox.Clear();
        }
    }

    private void AdminSmtpApiSyntaxTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded)
        {
            UpdateApiSyntaxUi();
        }
    }

    private void UpdateApiSyntaxUi()
    {
        SmtpApiReportSyntaxSetValidation validation = SmtpApiReportSyntaxSet.Validate(
            AdminSmtpApiSyntax1TextBox.Text,
            AdminSmtpApiSyntax2TextBox.Text,
            AdminSmtpApiSyntax3TextBox.Text);
        AdminSmtpApiSyntaxValidationTextBlock.Text = validation.ErrorMessage ?? string.Empty;
        AdminSmtpApiSyntaxPreviewTextBlock.Text = validation.IsValid
            ? "Voorbeeld: " + string.Join(
                "  |  ",
                SmtpApiReportSyntaxSet
                    .Resolve(
                        AdminSmtpApiSyntax1TextBox.Text,
                        AdminSmtpApiSyntax2TextBox.Text,
                        AdminSmtpApiSyntax3TextBox.Text)
                    .Select(template => SmtpPortalReportNameSyntax.BuildExample(template)))
            : "Voorbeeld is beschikbaar zodra alle ingevulde syntaxen geldig zijn.";
    }

    private void UpdateApiLastUsedStatus()
    {
        AdminSmtpApiLastUsedTextBlock.Text = _storedApiConfig.LastSuccessfulUseAtUtc.HasValue
            ? "API laatst succesvol gebruikt: " +
              _storedApiConfig.LastSuccessfulUseAtUtc.Value.ToLocalTime().ToString("dd-MM-yyyy HH:mm")
            : "API nog niet succesvol gebruikt.";
    }

    private SmtpApiAdminSettingsInput ReadApiInput()
    {
        return new SmtpApiAdminSettingsInput(
            AdminSmtpApiKeyBox.Password,
            AdminSmtpApiChannelComboBox.Text ?? string.Empty,
            AdminSmtpApiSyntax1TextBox.Text,
            AdminSmtpApiSyntax2TextBox.Text,
            AdminSmtpApiSyntax3TextBox.Text);
    }

    private bool TrySaveApiSettingsOnly()
    {
        try
        {
            SmtpApiConfig updated = SmtpApiAdminConfigBuilder.Build(
                _smtpApiStore.LoadConfig(),
                ReadApiInput());
            _smtpApiStore.SaveConfig(updated);
            _storedApiConfig = updated;
            AdminSmtpApiSyntaxValidationTextBlock.Text = string.Empty;
            AdminSmtpApiKeySavedTextBlock.Text = updated.HasApiKey
                ? "******** versleuteld opgeslagen."
                : "Nog niet opgeslagen.";
            if (updated.HasApiKey && string.IsNullOrEmpty(AdminSmtpApiKeyBox.Password))
            {
                AdminSmtpApiKeyBox.Password = SmtpApiAdminConfigBuilder.StoredSecretPlaceholder;
            }

            return true;
        }
        catch (InvalidOperationException ex)
        {
            AdminSmtpApiSyntaxValidationTextBlock.Text = ex.Message;
            AdminSmtpApiStatusTextBlock.Text = "Instellingen niet opgeslagen: " + ex.Message;
            return false;
        }
    }

    private string? ResolveApiKeyForRequest()
    {
        if (!_storedApiConfig.HasApiKey)
        {
            AdminSmtpApiStatusTextBlock.Text = "Vul eerst een API-sleutel in.";
            return null;
        }

        try
        {
            return SmtpPortalSecretProtector.Unprotect(_storedApiConfig.EncryptedApiKey!);
        }
        catch (Exception ex)
        {
            AdminSmtpApiStatusTextBlock.Text = "API-sleutel kon niet worden gelezen: " + ex.Message;
            return null;
        }
    }

    private async void TestSmtpApiConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TrySaveApiSettingsOnly())
        {
            return;
        }

        string? apiKey = ResolveApiKeyForRequest();
        if (apiKey is null)
        {
            return;
        }

        SetApiBusy(true, "SMTP.com API-verbinding testen...");
        try
        {
            IReadOnlyList<SmtpApiChannel> channels =
                await _smtpApiClient.ListChannelsAsync(apiKey, CancellationToken.None);
            _smtpApiStore.RecordSuccessfulUse(DateTime.UtcNow);
            _storedApiConfig = _smtpApiStore.LoadConfig() with
            {
                ConnectionStatus = $"Verbinding geslaagd; {channels.Count} channel(s) gevonden."
            };
            _smtpApiStore.SaveConfig(_storedApiConfig);
            FillChannelComboBox(channels);
            UpdateApiLastUsedStatus();
            AdminSmtpApiStatusTextBlock.Text = _storedApiConfig.ConnectionStatus!;
        }
        catch (Exception ex)
        {
            MailLogInspectorLog.Error("smtp-api", "SMTP.com API-verbindingstest mislukt", ex);
            AdminSmtpApiStatusTextBlock.Text = "Verbinding mislukt: " + ex.Message;
        }
        finally
        {
            SetApiBusy(false, AdminSmtpApiStatusTextBlock.Text);
        }
    }

    private async void LoadSmtpApiChannelsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TrySaveApiSettingsOnly())
        {
            return;
        }

        string? apiKey = ResolveApiKeyForRequest();
        if (apiKey is null)
        {
            return;
        }

        SetApiBusy(true, "Channels ophalen...");
        try
        {
            IReadOnlyList<SmtpApiChannel> channels =
                await _smtpApiClient.ListChannelsAsync(apiKey, CancellationToken.None);
            FillChannelComboBox(channels);
            _smtpApiStore.RecordSuccessfulUse(DateTime.UtcNow);
            _storedApiConfig = _smtpApiStore.LoadConfig();
            UpdateApiLastUsedStatus();
            AdminSmtpApiStatusTextBlock.Text = $"{channels.Count} channel(s) opgehaald.";
        }
        catch (Exception ex)
        {
            MailLogInspectorLog.Error("smtp-api", "Channels ophalen mislukt", ex);
            AdminSmtpApiStatusTextBlock.Text = "Channels ophalen mislukt: " + ex.Message;
        }
        finally
        {
            SetApiBusy(false, AdminSmtpApiStatusTextBlock.Text);
        }
    }

    private void FillChannelComboBox(IReadOnlyList<SmtpApiChannel> channels)
    {
        string current = AdminSmtpApiChannelComboBox.Text ?? string.Empty;
        AdminSmtpApiChannelComboBox.ItemsSource = channels.Select(channel => channel.Name).ToArray();
        AdminSmtpApiChannelComboBox.Text = current;
    }

    private async void LoadSmtpApiReportsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TrySaveApiSettingsOnly())
        {
            return;
        }

        string? apiKey = ResolveApiKeyForRequest();
        if (apiKey is null)
        {
            return;
        }

        SetApiBusy(true, "Beschikbare rapporten ophalen...");
        try
        {
            IReadOnlyList<SmtpApiReport> reports =
                await _smtpApiClient.ListOndemandReportsAsync(apiKey, CancellationToken.None);
            IReadOnlyList<string> templates = SmtpApiReportSyntaxSet.Resolve(
                _storedApiConfig.ReportSyntax1,
                _storedApiConfig.ReportSyntax2,
                _storedApiConfig.ReportSyntax3);
            IReadOnlyList<SmtpApiSelectedReport> available =
                SmtpApiReportMatcher.ParseAvailable(reports, templates, _storedApiConfig.Channel);
            AdminSmtpApiReportsListBox.ItemsSource = available
                .Select(report => new SmtpApiReportListItem(report))
                .ToArray();
            _smtpApiStore.RecordSuccessfulUse(DateTime.UtcNow);
            _storedApiConfig = _smtpApiStore.LoadConfig();
            UpdateApiLastUsedStatus();
            AdminSmtpApiStatusTextBlock.Text = available.Count == 0
                ? $"Geen passende rapporten gevonden ({reports.Count} on-demand rapporten bekeken)."
                : $"{available.Count} passend(e) rapport(en) gevonden. Selecteer wat je wilt importeren.";
        }
        catch (Exception ex)
        {
            MailLogInspectorLog.Error("smtp-api", "Rapporten ophalen via API mislukt", ex);
            AdminSmtpApiStatusTextBlock.Text = "Rapporten ophalen mislukt: " + ex.Message;
        }
        finally
        {
            SetApiBusy(false, AdminSmtpApiStatusTextBlock.Text);
        }
    }

    private async void ImportSelectedSmtpApiReportsButton_Click(object sender, RoutedEventArgs e)
    {
        SmtpApiSelectedReport[] selected = AdminSmtpApiReportsListBox.SelectedItems
            .OfType<SmtpApiReportListItem>()
            .Select(item => item.Report)
            .ToArray();
        if (selected.Length == 0)
        {
            AdminSmtpApiStatusTextBlock.Text = "Selecteer eerst een of meer rapporten.";
            return;
        }

        if (!TrySaveApiSettingsOnly())
        {
            return;
        }

        SetApiBusy(true, "Geselecteerde rapporten downloaden en importeren...");
        try
        {
            var mailStore = new MailLogInspectorStore(_workspace.DatabasePath);
            mailStore.Initialize();
            var importService = new MailLogInspectorImportService(mailStore, _workspace);
            var source = new SmtpApiReportSyncSource(
                _smtpApiStore,
                _syncStore,
                mailStore,
                new GmailZipImportRunner(importService),
                _smtpApiClient,
                _workspace);
            var progress = new Progress<string>(message =>
                AdminSmtpApiStatusTextBlock.Text = message);
            ReportSyncSourceResult result = await source.ImportSelectedAsync(
                selected,
                CancellationToken.None,
                progress);
            _storedApiConfig = _smtpApiStore.LoadConfig();
            UpdateApiLastUsedStatus();
            AdminSmtpApiStatusTextBlock.Text = "Import gereed. " + result.Summary;
        }
        catch (Exception ex)
        {
            MailLogInspectorLog.Error("smtp-api", "Import van geselecteerde API-rapporten mislukt", ex);
            AdminSmtpApiStatusTextBlock.Text = "Import mislukt: " + ex.Message;
        }
        finally
        {
            SetApiBusy(false, AdminSmtpApiStatusTextBlock.Text);
        }
    }

    private void SetApiBusy(bool busy, string status)
    {
        AdminSmtpApiStatusTextBlock.Text = status;
        AdminSmtpApiProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        SetSharedControlsEnabled(!busy);
    }

    private sealed class SmtpApiReportListItem
    {
        public SmtpApiReportListItem(SmtpApiSelectedReport report)
        {
            Report = report;
        }

        public SmtpApiSelectedReport Report { get; }

        public override string ToString()
        {
            return $"{Report.PeriodStart:dd-MM-yyyy} → {Report.PeriodEnd:dd-MM-yyyy}  |  syntax {Report.SyntaxSlot}  |  {Report.Name}";
        }
    }

    private void AdminSmtpPortalSecretBox_GotKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        if (sender is PasswordBox passwordBox &&
            string.Equals(
                passwordBox.Password,
                SmtpPortalAdminConfigBuilder.StoredSecretPlaceholder,
                StringComparison.Ordinal))
        {
            passwordBox.Clear();
        }
    }

    private void AdminImapSecretBox_GotKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        if (sender is PasswordBox passwordBox &&
            string.Equals(
                passwordBox.Password,
                GmailAdminConfigBuilder.StoredSecretPlaceholder,
                StringComparison.Ordinal))
        {
            passwordBox.Clear();
        }
    }

    private void AdminGmailAuthModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateAuthenticationPanels();
    }

    private void AdminImapProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateAuthenticationPanels();
    }

    private void UpdateAuthenticationPanels()
    {
        if (AdminGmailOAuthComboBoxItem is null)
        {
            return;
        }

        bool gmail = string.Equals(SelectedImapProvider, ImapProvider.Gmail, StringComparison.OrdinalIgnoreCase);
        AdminGmailOAuthComboBoxItem.Visibility = gmail ? Visibility.Visible : Visibility.Collapsed;
        if (!gmail && string.Equals(SelectedAuthenticationMode, GmailAuthenticationMode.OAuth, StringComparison.OrdinalIgnoreCase))
        {
            SelectAuthenticationMode(GmailAuthenticationMode.AppPassword);
        }

        bool oauth = gmail && string.Equals(SelectedAuthenticationMode, GmailAuthenticationMode.OAuth, StringComparison.OrdinalIgnoreCase);
        bool custom = string.Equals(SelectedImapProvider, ImapProvider.Custom, StringComparison.OrdinalIgnoreCase);
        AdminOAuthPanel.Visibility = oauth ? Visibility.Visible : Visibility.Collapsed;
        AdminAppPasswordPanel.Visibility = oauth ? Visibility.Collapsed : Visibility.Visible;
        AdminCustomImapPanel.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        AdminCustomImapUseSslCheckBox.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        AdminImapPasswordLabel.Text = gmail ? "Gmail app-wachtwoord" : "IMAP-wachtwoord";
    }
    private async void TestAdminConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        GmailAdminSettingsInput input = ReadInput();
        if (string.IsNullOrWhiteSpace(input.AccountEmailAddress))
        {
            AdminConnectionStatusTextBlock.Text = "Vul eerst het e-mailadres in.";
            return;
        }

        GmailReportConfig candidate = GmailAdminConfigBuilder.Build(_storedConfig, input);
        SetBusy(true, "IMAP-verbinding testen...");
        try
        {
            if (string.Equals(candidate.AuthenticationMode, GmailAuthenticationMode.AppPassword, StringComparison.OrdinalIgnoreCase))
            {
                bool storedPasswordVisible = string.Equals(
                    input.AppPassword,
                    GmailAdminConfigBuilder.StoredSecretPlaceholder,
                    StringComparison.Ordinal);
                string appPassword = string.IsNullOrWhiteSpace(input.AppPassword) || storedPasswordVisible
                    ? GmailOAuthService.UnprotectSecret(candidate.EncryptedAppPassword ?? string.Empty)
                    : input.AppPassword.Trim();
                if (string.IsNullOrWhiteSpace(appPassword))
                {
                    throw new InvalidOperationException("Vul eerst het IMAP-wachtwoord in.");
                }

                GmailImapConnectionSettings settings = new(
                    candidate.AccountEmailAddress!,
                    GmailAuthenticationMode.AppPassword,
                    null,
                    appPassword,
                    candidate.ImapHost ?? string.Empty,
                    candidate.ImapPort,
                    candidate.ImapUseSsl);
                await _mailClient.FetchInboxCandidatesAsync(settings, CancellationToken.None);
                _connectionStatus = "IMAP-wachtwoord getest";
            }
            else
            {
                string clientSecret = string.IsNullOrWhiteSpace(input.ClientSecret)
                    ? GmailOAuthService.UnprotectClientSecret(_storedConfig.ClientSecret ?? string.Empty)
                    : input.ClientSecret.Trim();
                if (string.IsNullOrWhiteSpace(input.ClientId) || string.IsNullOrWhiteSpace(clientSecret))
                {
                    throw new InvalidOperationException("Vul eerst client id en client secret in.");
                }

                GmailOAuthTokenEnvelope token = await _oauthService.AuthorizeInteractiveAsync(
                    new GmailOAuthConfig(input.AccountEmailAddress.Trim(), input.ClientId.Trim(), clientSecret, null),
                    CancellationToken.None);
                _pendingRefreshToken = GmailOAuthService.ProtectRefreshToken(token.RefreshToken);
                _connectionStatus = "Google OAuth getest";
            }

            AdminConnectionStatusTextBlock.Text = "IMAP-verbinding geslaagd.";
        }
        catch (Exception ex)
        {
            AdminConnectionStatusTextBlock.Text = "IMAP-verbinding mislukt: " + ex.Message;
        }
        finally
        {
            SetBusy(false, AdminConnectionStatusTextBlock.Text);
        }
    }
    private async void RunSmtpPortalProbeButton_Click(object sender, RoutedEventArgs e)
    {
        await RunSmtpPortalProbeAsync(visible: false);
    }

    private async void RunVisibleSmtpPortalProbeButton_Click(object sender, RoutedEventArgs e)
    {
        await RunSmtpPortalProbeAsync(visible: true);
    }

    private async Task RunSmtpPortalProbeAsync(bool visible)
    {
        if (!TryBuildPortalConfig(out SmtpPortalConfig portalConfig))
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(portalConfig.Username) ||
            string.IsNullOrWhiteSpace(portalConfig.EncryptedPassword) ||
            string.IsNullOrWhiteSpace(portalConfig.EncryptedTotpSecret))
        {
            AdminSmtpPortalStatusTextBlock.Text = "Vul SMTP.com-gebruikersnaam, wachtwoord en MFA-secret in.";
            return;
        }

        _smtpPortalStore.SaveConfig(portalConfig);
        _storedPortalConfig = portalConfig;
        UpdatePortalSecretSavedStatus();
        SetPortalBusy(true, visible ? "Zichtbare SMTP.com-diagnose starten..." : "SMTP.com-proefdownload starten...");
        try
        {
            string userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Mail Log Inspector",
                "WebView2",
                "SmtpPortal");
            string incomingDirectory = Path.Combine(_workspace.IncomingDirectory, "SmtpPortalProbe");
            var browser = new SmtpPortalBrowserWindow(userDataFolder);
            if (visible)
            {
                browser.Owner = this;
            }

            var mailStore = new MailLogInspectorStore(_workspace.DatabasePath);
            DateTime? latestSuccessfulDay = mailStore.ReadLatestDailyImportReportDayReadOnly();
            var service = new SmtpPortalProbeService(
                _smtpPortalStore,
                mailStore,
                incomingDirectory,
                browser);
            var progress = new Progress<string>(status => AdminSmtpPortalStatusTextBlock.Text = status);
            SmtpPortalProbeResult result = await service.DownloadLatestReportAsync(
                portalConfig,
                latestSuccessfulDay,
                visible,
                CancellationToken.None,
                progress);

            _storedPortalConfig = _smtpPortalStore.LoadConfig();
            UpdatePortalLastUsedStatus();
            AdminSmtpPortalStatusTextBlock.Text = result.AlreadyImported
                ? $"Proefdownload geslaagd: {result.PeriodStart:dd-MM-yyyy}; bestand was al geïmporteerd."
                : $"Proefdownload geslaagd: {result.PeriodStart:dd-MM-yyyy}; nieuw bestand is niet geïmporteerd.";
        }
        catch (Exception ex)
        {
            MailLogInspectorLog.Error("smtp-portal", "SMTP.com-proefdownload mislukt", ex);
            _storedPortalConfig = _smtpPortalStore.LoadConfig();
            UpdatePortalLastUsedStatus();
            AdminSmtpPortalStatusTextBlock.Text = "SMTP.com-proefdownload mislukt: " + ex.Message +
                (visible
                    ? " Diagnosevenster blijft open; rond zo nodig de login af en start daarna de zichtbare diagnose opnieuw."
                    : string.Empty);
        }
        finally
        {
            SetPortalBusy(false, AdminSmtpPortalStatusTextBlock.Text);
        }
    }

    private async void RunReportSyncNowButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TrySaveCurrentSettings())
        {
            return;
        }

        GmailReportConfig gmailConfig = _store.LoadConfig();
        SmtpPortalConfig portalConfig = _smtpPortalStore.LoadConfig();
        SmtpApiConfig apiConfig = _smtpApiStore.LoadConfig();
        if (!IsSelectedSourceReady(_storedSyncConfig.Mode, gmailConfig, portalConfig, apiConfig))
        {
            AdminReportSyncStatusTextBlock.Text = "De gekozen synchronisatiebron is nog niet volledig ingesteld.";
            return;
        }

        var cancellation = new CancellationTokenSource();
        _reportSyncCancellation = cancellation;
        SetReportSyncBusy(true, "Download en import worden gestart...");
        try
        {
            var progress = new Progress<string>(message =>
                AdminReportSyncStatusTextBlock.Text = message);
            ReportSyncSourceResult result = await _reportSyncRunner.RunAsync(
                cancellation.Token,
                progress);
            AdminReportSyncStatusTextBlock.Text = "Synchronisatie gereed. " + result.Summary;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            AdminReportSyncStatusTextBlock.Text = "Synchronisatie gestopt.";
        }
        catch (Exception ex)
        {
            MailLogInspectorLog.Error("sync", "Handmatige synchronisatie vanuit admin mislukt", ex);
            AdminReportSyncStatusTextBlock.Text = "Synchronisatie mislukt: " + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_reportSyncCancellation, cancellation))
            {
                _reportSyncCancellation = null;
                SetReportSyncBusy(false, AdminReportSyncStatusTextBlock.Text);
            }
            cancellation.Dispose();
        }
    }

    private void CancelReportSyncButton_Click(object sender, RoutedEventArgs e)
    {
        CancelReportSyncButton.IsEnabled = false;
        AdminReportSyncStatusTextBlock.Text = "Synchronisatie stoppen...";
        _reportSyncCancellation?.Cancel();
    }

    private void SaveAdminSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (TrySaveCurrentSettings())
        {
            DialogResult = true;
        }
    }

    private bool TrySavePortalSettingsOnly()
    {
        if (!TryBuildPortalConfig(out SmtpPortalConfig updatedPortal))
        {
            return false;
        }

        _smtpPortalStore.SaveConfig(updatedPortal);
        _storedPortalConfig = updatedPortal;
        UpdatePortalSecretSavedStatus();
        return true;
    }

    private bool TrySaveCurrentSettings()
    {
        if (!TrySavePortalSettingsOnly() || !TrySaveApiSettingsOnly())
        {
            return false;
        }

        GmailAdminSettingsInput input = ReadInput();
        if (!string.IsNullOrWhiteSpace(input.AccountEmailAddress))
        {
            GmailReportConfig updatedGmail = GmailAdminConfigBuilder.Build(_storedConfig, input) with
            {
                EncryptedRefreshToken = _pendingRefreshToken ?? _storedConfig.EncryptedRefreshToken,
                ConnectedAtUtc = _storedConfig.ConnectedAtUtc ?? DateTime.UtcNow,
                LastTokenRefreshAtUtc = _pendingRefreshToken is null ? _storedConfig.LastTokenRefreshAtUtc : DateTime.UtcNow,
                ConnectionStatus = _connectionStatus ?? _storedConfig.ConnectionStatus
            };
            _store.SaveConfig(updatedGmail);
            _storedConfig = updatedGmail;
            UpdateImapSecretSavedStatus();
            UpdateAuthenticationPanels();
        }

        // Reload first so a sync completed while this dialog was open keeps its timestamps.
        ReportSyncConfig currentSyncConfig = _syncStore.LoadConfig();
        _storedSyncConfig = AdminSyncConfigBuilder.Build(currentSyncConfig, ReadSyncInput());
        _syncStore.SaveConfig(_storedSyncConfig);
        return true;
    }

    private static bool IsSelectedSourceReady(
        string mode,
        GmailReportConfig gmailConfig,
        SmtpPortalConfig portalConfig,
        SmtpApiConfig apiConfig)
    {
        bool gmailReady = IsGmailConfigReady(gmailConfig);
        bool directReady = !string.IsNullOrWhiteSpace(portalConfig.Username) &&
                           !string.IsNullOrWhiteSpace(portalConfig.EncryptedPassword) &&
                           !string.IsNullOrWhiteSpace(portalConfig.EncryptedTotpSecret);
        bool apiReady = apiConfig.HasApiKey;
        return ReportSyncMode.Normalize(mode) switch
        {
            ReportSyncMode.ApiOnly => apiReady,
            ReportSyncMode.ApiWithImapFallback => apiReady || gmailReady,
            ReportSyncMode.DirectOnly => directReady,
            ReportSyncMode.DirectWithGmailFallback => directReady || gmailReady,
            _ => gmailReady
        };
    }

    private static bool IsGmailConfigReady(GmailReportConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.AccountEmailAddress) ||
            (string.Equals(ImapProvider.Normalize(config.ImapProvider), ImapProvider.Custom, StringComparison.OrdinalIgnoreCase) &&
             string.IsNullOrWhiteSpace(config.ImapHost)))
        {
            return false;
        }

        return string.Equals(config.AuthenticationMode, GmailAuthenticationMode.AppPassword, StringComparison.OrdinalIgnoreCase)
            ? !string.IsNullOrWhiteSpace(config.EncryptedAppPassword)
            : !string.IsNullOrWhiteSpace(config.ClientId) &&
              !string.IsNullOrWhiteSpace(config.ClientSecret) &&
              !string.IsNullOrWhiteSpace(config.EncryptedRefreshToken);
    }

    private void CancelAdminSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void UpdatePortalLastUsedStatus()
    {
        AdminSmtpPortalLastUsedTextBlock.Text =
            _storedPortalConfig.LastSuccessfulPortalUseAtUtc.HasValue
                ? "Portaalsessie laatst succesvol gebruikt: " +
                  _storedPortalConfig.LastSuccessfulPortalUseAtUtc.Value
                      .ToLocalTime()
                      .ToString("dd-MM-yyyy HH:mm")
                : "Portaalsessie nog niet succesvol gebruikt.";
    }
    private void SetBusy(bool busy, string status)
    {
        AdminConnectionStatusTextBlock.Text = status;
        AdminConnectionProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        SetSharedControlsEnabled(!busy);
    }

    private void SetPortalBusy(bool busy, string status)
    {
        AdminSmtpPortalStatusTextBlock.Text = status;
        AdminSmtpPortalProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        SetSharedControlsEnabled(!busy);
    }

    private void SetReportSyncBusy(bool busy, string status)
    {
        AdminReportSyncStatusTextBlock.Text = status;
        AdminReportSyncProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CancelReportSyncButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CancelReportSyncButton.IsEnabled = busy;
        SetSharedControlsEnabled(!busy);
    }

    private void SetSharedControlsEnabled(bool enabled)
    {
        TestAdminConnectionButton.IsEnabled = enabled;
        RunSmtpPortalProbeButton.IsEnabled = enabled;
        RunVisibleSmtpPortalProbeButton.IsEnabled = enabled;
        RunReportSyncNowButton.IsEnabled = enabled;
        TestSmtpApiConnectionButton.IsEnabled = enabled;
        LoadSmtpApiChannelsButton.IsEnabled = enabled;
        LoadSmtpApiReportsButton.IsEnabled = enabled;
        ImportSelectedSmtpApiReportsButton.IsEnabled = enabled;
        SaveAdminSettingsButton.IsEnabled = enabled;
        CancelAdminSettingsButton.IsEnabled = enabled;
    }
}