using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using MailLogInspector.Core;
using Microsoft.Web.WebView2.Core;

namespace MailLogInspector.App;

public partial class SmtpPortalBrowserWindow : Window, ISmtpPortalBrowser
{
    private static readonly Uri ReportsUri = new("https://my.smtp.com/reporting?tab=reports");
    private readonly string _userDataFolder;
    private readonly List<CoreWebView2Frame> _frames = [];
    private SmtpPortalCredentials? _credentials;
    private bool _cookieConsentHandled;
    private bool _disposed;

    public SmtpPortalBrowserWindow(string userDataFolder)
    {
        InitializeComponent();
        _userDataFolder = Path.GetFullPath(userDataFolder);
    }

    public async Task InitializeAsync(
        SmtpPortalCredentials credentials,
        bool visible,
        CancellationToken cancellationToken)
    {
        _credentials = credentials;
        ConfigureVisibility(visible);
        Show();

        Directory.CreateDirectory(_userDataFolder);
        CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: _userDataFolder);
        await PortalWebView.EnsureCoreWebView2Async(environment);
        PortalWebView.CoreWebView2.Settings.AreDevToolsEnabled = visible;
        PortalWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = visible;
        PortalWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        PortalWebView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
        PortalWebView.CoreWebView2.FrameCreated += CoreWebView2_FrameCreated;

        SetStatus("My reports openen...");
        PortalWebView.CoreWebView2.Navigate(ReportsUri.AbsoluteUri);
        await CompleteAuthenticationAsync(cancellationToken);
    }

    public async Task SetPageSizeAsync(int pageSize, CancellationToken cancellationToken)
    {
        if (pageSize != 100)
        {
            throw new InvalidOperationException("Alleen 100 rapporten per pagina is toegestaan voor inhalen.");
        }

        SetStatus("100 rapporten op pagina 1 tonen...");
        string currentValue = await ExecuteStringAsync(
            """
            (() => {
                const nodes = [...document.querySelectorAll('*')];
                const match = nodes.find(node =>
                    node.children.length === 0 &&
                    /^\s*\d+\s*\/\s*page\s*$/i.test(node.textContent || ''));
                return match ? (match.textContent || '').trim() : '';
            })()
            """,
            cancellationToken);
        if (string.Equals(NormalizeSpace(currentValue), "100 / page", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        bool opened = await ExecuteBooleanAsync(
            """
            (() => {
                const nodes = [...document.querySelectorAll('*')];
                const label = nodes.find(node =>
                    node.children.length === 0 &&
                    /^\s*\d+\s*\/\s*page\s*$/i.test(node.textContent || ''));
                if (!label) return false;
                const clickable = label.closest('[role="combobox"], .ant-select-selector, button') || label;
                clickable.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
                clickable.click();
                return true;
            })()
            """,
            cancellationToken);
        if (!opened)
        {
            throw new InvalidOperationException("De paginagrootte van My reports kon niet worden geopend.");
        }

        await Task.Delay(350, cancellationToken);
        bool selected = await ExecuteBooleanAsync(
            """
            (() => {
                const nodes = [...document.querySelectorAll('[role="option"], .ant-select-item-option, body *')];
                const option = nodes.find(node =>
                    node.children.length === 0 &&
                    /^\s*100\s*\/\s*page\s*$/i.test(node.textContent || '') &&
                    node.offsetParent !== null);
                if (!option) return false;
                option.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
                option.click();
                return true;
            })()
            """,
            cancellationToken);
        if (!selected)
        {
            throw new InvalidOperationException("De optie 100 / page werd niet gevonden.");
        }

        await Task.Delay(800, cancellationToken);
    }

    public async Task<IReadOnlyList<SmtpPortalReportRow>> ReadFirstPageReportsAsync(
        CancellationToken cancellationToken)
    {
        SetStatus("Rapportregels op pagina 1 controleren...");
        for (int attempt = 0; attempt < 40; attempt++)
        {
            string json = await PortalWebView.CoreWebView2.ExecuteScriptAsync(
                SmtpPortalReportDomScripts.ReadFirstPageReports);
            cancellationToken.ThrowIfCancellationRequested();
            List<SmtpPortalReportRow> rows = JsonSerializer.Deserialize<List<SmtpPortalReportRow>>(
                                                json,
                                                new JsonSerializerOptions
                                                {
                                                    PropertyNameCaseInsensitive = true
                                                })
                                            ?? [];
            if (rows.Count > 0)
            {
                return rows;
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new InvalidOperationException(
            "De SMTP.com-rapportlijst is niet geladen of de pagina-opbouw is gewijzigd.");
    }

    public async Task<string> DownloadAsync(
        SmtpPortalReport report,
        string temporaryDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(temporaryDirectory);
        string resultPath = Path.Combine(temporaryDirectory, Guid.NewGuid().ToString("N") + ".zip");
        var downloadStarted = new TaskCompletionSource<CoreWebView2DownloadOperation>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void HandleDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs args)
        {
            args.ResultFilePath = resultPath;
            args.Handled = true;
            downloadStarted.TrySetResult(args.DownloadOperation);
        }

        PortalWebView.CoreWebView2.DownloadStarting += HandleDownloadStarting;
        try
        {
            SetStatus($"Rapport {report.PeriodStart:dd-MM-yyyy} downloaden...");
            await Task.Delay(650, cancellationToken);
            bool clicked = await ExecuteBooleanAsync(
                SmtpPortalReportDomScripts.BuildDownloadClick(report.Name),
                cancellationToken);
            if (!clicked)
            {
                Task first = await Task.WhenAny(
                    downloadStarted.Task,
                    Task.Delay(1500, cancellationToken));
                if (first != downloadStarted.Task)
                {
                    throw new InvalidOperationException("De downloadknop van het gekozen rapport werd niet gevonden.");
                }
            }

            CoreWebView2DownloadOperation operation = await downloadStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(30),
                cancellationToken);
            await WaitForDownloadAsync(operation, cancellationToken);
            return resultPath;
        }
        finally
        {
            PortalWebView.CoreWebView2.DownloadStarting -= HandleDownloadStarting;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        PortalWebView.CoreWebView2?.Stop();
        Close();
        return ValueTask.CompletedTask;
    }

    private async Task CompleteAuthenticationAsync(CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(90);
        DateTime? credentialsSubmittedAtUtc = null;
        DateTime? totpSubmittedAtUtc = null;
        int totpIndex = 0;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WaitForDocumentAsync(cancellationToken);
            if (await TryRejectCookieConsentAsync(cancellationToken))
            {
                credentialsSubmittedAtUtc = null;
                totpSubmittedAtUtc = null;
                await Task.Delay(500, cancellationToken);
                continue;
            }

            PortalPageState state = await ReadPageStateAsync(cancellationToken);
            if (state.IsReportsPage)
            {
                SetStatus("My reports is gereed.");
                return;
            }

            if (state.HasCaptcha)
            {
                throw new InvalidOperationException("SMTP.com vraagt een aanvullende beveiligingscontrole. Open de zichtbare diagnose.");
            }

            if (state.HasUsername && state.HasPassword)
            {
                if (credentialsSubmittedAtUtc.HasValue)
                {
                    if (SmtpPortalAuthenticationTiming.IsCredentialSubmissionPending(
                            credentialsSubmittedAtUtc.Value,
                            DateTime.UtcNow))
                    {
                        SetStatus("Aanmelding verwerken...");
                        await Task.Delay(350, cancellationToken);
                        continue;
                    }

                    throw new InvalidOperationException("SMTP.com heeft de gebruikersnaam of het wachtwoord geweigerd.");
                }

                SetStatus("Aanmelden bij SMTP.com...");
                await SubmitCredentialsAsync(cancellationToken);
                credentialsSubmittedAtUtc = DateTime.UtcNow;
                await Task.Delay(350, cancellationToken);
                continue;
            }

            if (state.HasOneTimePassword)
            {
                if (totpSubmittedAtUtc.HasValue &&
                    SmtpPortalAuthenticationTiming.IsTotpSubmissionPending(
                        totpSubmittedAtUtc.Value,
                        DateTime.UtcNow))
                {
                    SetStatus("MFA-resultaat verwerken...");
                    await Task.Delay(350, cancellationToken);
                    continue;
                }

                totpSubmittedAtUtc = null;
                if (_credentials is null || totpIndex >= _credentials.TotpCodes.Count)
                {
                    throw new InvalidOperationException("SMTP.com heeft de MFA-code geweigerd.");
                }

                SetStatus("MFA-code controleren...");
                await SubmitTotpAsync(_credentials.TotpCodes[totpIndex], cancellationToken);
                totpIndex++;
                totpSubmittedAtUtc = DateTime.UtcNow;
                await Task.Delay(350, cancellationToken);
                continue;
            }

            if (state.IsAuthenticated)
            {
                PortalWebView.CoreWebView2.Navigate(ReportsUri.AbsoluteUri);
                await Task.Delay(500, cancellationToken);
                continue;
            }

            await Task.Delay(350, cancellationToken);
        }

        throw new TimeoutException("SMTP.com My reports werd niet binnen 90 seconden geopend.");
    }

    private async Task SubmitCredentialsAsync(CancellationToken cancellationToken)
    {
        if (_credentials is null)
        {
            throw new InvalidOperationException("SMTP.com-inloggegevens ontbreken.");
        }

        string usernameJson = JsonSerializer.Serialize(_credentials.Username);
        string passwordJson = JsonSerializer.Serialize(_credentials.Password);
        bool submitted = await ExecuteBooleanAsync(
            $$"""
            (() => {
                const username = {{usernameJson}};
                const password = {{passwordJson}};
                const inputs = [...document.querySelectorAll('input')];
                const userInput = inputs.find(input =>
                    input.type === 'email' ||
                    /user|email/i.test([
                        input.name || '',
                        input.id || '',
                        input.placeholder || '',
                        input.autocomplete || ''
                    ].join(' ')));
                const passwordInput = inputs.find(input => input.type === 'password');
                const button = [...document.querySelectorAll('button, input[type="submit"]')]
                    .find(control => /login|sign in/i.test(control.innerText || control.value || ''));
                if (!userInput || !passwordInput || !button) return false;
                const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
                setter.call(userInput, username);
                setter.call(passwordInput, password);
                [userInput, passwordInput].forEach(input => {
                    input.dispatchEvent(new Event('input', { bubbles: true }));
                    input.dispatchEvent(new Event('change', { bubbles: true }));
                });
                button.click();
                return true;
            })()
            """,
            cancellationToken);
        if (!submitted)
        {
            throw new InvalidOperationException("Het SMTP.com-inlogformulier is gewijzigd.");
        }
    }

    private async Task<bool> TryRejectCookieConsentAsync(CancellationToken cancellationToken)
    {
        if (_cookieConsentHandled)
        {
            return false;
        }

        string script = SmtpPortalCookieConsentScript.RejectAll;

        if (await ExecuteBooleanAsync(script, cancellationToken))
        {
            _cookieConsentHandled = true;
            SetStatus("Cookiemelding afgewezen...");
            return true;
        }

        foreach (CoreWebView2Frame frame in _frames.ToArray())
        {
            if (frame.IsDestroyed() != 0)
            {
                continue;
            }

            try
            {
                string json = await frame.ExecuteScriptAsync(script);
                cancellationToken.ThrowIfCancellationRequested();
                if (SmtpPortalScriptResultParser.ParseBoolean(json))
                {
                    _cookieConsentHandled = true;
                    SetStatus("Cookiemelding afgewezen...");
                    return true;
                }
            }
            catch (Exception ex) when (
                ex is COMException or InvalidOperationException or ObjectDisposedException)
            {
                // Consent frames can disappear while their rejection is being processed.
            }
        }

        return false;
    }

    private async Task SubmitTotpAsync(string code, CancellationToken cancellationToken)
    {
        string codeJson = JsonSerializer.Serialize(code);
        bool submitted = await ExecuteBooleanAsync(
            $$"""
            (() => {
                const code = {{codeJson}};
                const inputs = [...document.querySelectorAll('input')];
                const otpInput = inputs.find(input =>
                    /one.?time|otp|authenticator|123456/i.test([
                        input.name || '',
                        input.id || '',
                        input.placeholder || '',
                        input.autocomplete || ''
                    ].join(' ')));
                const button = [...document.querySelectorAll('button, input[type="submit"]')]
                    .find(control => /login|verify|continue/i.test(control.innerText || control.value || ''));
                if (!otpInput || !button) return false;
                const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
                setter.call(otpInput, code);
                otpInput.dispatchEvent(new Event('input', { bubbles: true }));
                otpInput.dispatchEvent(new Event('change', { bubbles: true }));
                button.click();
                return true;
            })()
            """,
            cancellationToken);
        if (!submitted)
        {
            throw new InvalidOperationException("Het SMTP.com-MFA-formulier is gewijzigd.");
        }
    }

    private async Task<PortalPageState> ReadPageStateAsync(CancellationToken cancellationToken)
    {
        string json = await PortalWebView.CoreWebView2.ExecuteScriptAsync(
            """
            (() => {
                const inputs = [...document.querySelectorAll('input')];
                const bodyText = document.body ? document.body.innerText : '';
                return {
                    isReportsPage:
                        location.hostname === 'my.smtp.com' &&
                        location.pathname === '/reporting' &&
                        new URLSearchParams(location.search).get('tab') === 'reports' &&
                        /My reports/i.test(bodyText),
                    isAuthenticated: /Logout/i.test(bodyText) && /Reporting/i.test(bodyText),
                    hasUsername: inputs.some(input =>
                        input.type === 'email' ||
                        /user|email/i.test((input.name || '') + ' ' + (input.placeholder || ''))),
                    hasPassword: inputs.some(input => input.type === 'password'),
                    hasOneTimePassword: inputs.some(input =>
                        /one.?time|otp|authenticator|123456/i.test(
                            (input.name || '') + ' ' +
                            (input.id || '') + ' ' +
                            (input.placeholder || '') + ' ' +
                            (input.autocomplete || ''))),
                    hasCaptcha: /captcha|security check|verify you are human/i.test(bodyText)
                };
            })()
            """);
        cancellationToken.ThrowIfCancellationRequested();
        return JsonSerializer.Deserialize<PortalPageState>(
                   json,
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? new PortalPageState();
    }

    private async Task WaitForDocumentAsync(CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            string state = await ExecuteStringAsync("document.readyState", cancellationToken);
            if (string.Equals(state, "complete", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(state, "interactive", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await Task.Delay(100, cancellationToken);
        }
    }

    private async Task<bool> ExecuteBooleanAsync(string script, CancellationToken cancellationToken)
    {
        string json = await PortalWebView.CoreWebView2.ExecuteScriptAsync(script);
        cancellationToken.ThrowIfCancellationRequested();
        return SmtpPortalScriptResultParser.ParseBoolean(json);
    }

    private async Task<string> ExecuteStringAsync(string script, CancellationToken cancellationToken)
    {
        string json = await PortalWebView.CoreWebView2.ExecuteScriptAsync(script);
        cancellationToken.ThrowIfCancellationRequested();
        return JsonSerializer.Deserialize<string>(json) ?? string.Empty;
    }

    private static async Task WaitForDownloadAsync(
        CoreWebView2DownloadOperation operation,
        CancellationToken cancellationToken)
    {
        if (operation.TotalBytesToReceive > MailLogInspectorImportLimits.MaxZipBytes)
        {
            operation.Cancel();
            throw new InvalidDataException(
                "Het SMTP.com-rapport is groter dan de toegestane downloadlimiet.");
        }
        if (operation.State == CoreWebView2DownloadState.Completed)
        {
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void HandleBytesReceivedChanged(object? sender, object args)
        {
            if (operation.BytesReceived <= MailLogInspectorImportLimits.MaxZipBytes)
            {
                return;
            }

            operation.Cancel();
            completion.TrySetException(new InvalidDataException(
                "Het SMTP.com-rapport is groter dan de toegestane downloadlimiet."));
        }
        void HandleStateChanged(object? sender, object args)
        {
            if (operation.State == CoreWebView2DownloadState.Completed)
            {
                completion.TrySetResult();
            }
            else if (operation.State == CoreWebView2DownloadState.Interrupted)
            {
                completion.TrySetException(new IOException(
                    "SMTP.com-download onderbroken: " + operation.InterruptReason));
            }
        }

        operation.StateChanged += HandleStateChanged;
        operation.BytesReceivedChanged += HandleBytesReceivedChanged;
        try
        {
            HandleBytesReceivedChanged(null, EventArgs.Empty);
            HandleStateChanged(null, EventArgs.Empty);
            await completion.Task.WaitAsync(TimeSpan.FromMinutes(3), cancellationToken);
        }
        finally
        {
            operation.StateChanged -= HandleStateChanged;
            operation.BytesReceivedChanged -= HandleBytesReceivedChanged;
        }
    }

    private void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (Uri.TryCreate(args.Uri, UriKind.Absolute, out Uri? uri) &&
            !string.Equals(uri.Host, "my.smtp.com", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, "about", StringComparison.OrdinalIgnoreCase))
        {
            args.Cancel = true;
        }
    }

    private void CoreWebView2_FrameCreated(object? sender, CoreWebView2FrameCreatedEventArgs args)
    {
        TrackFrame(args.Frame);
    }

    private void CoreWebView2Frame_FrameCreated(object? sender, CoreWebView2FrameCreatedEventArgs args)
    {
        TrackFrame(args.Frame);
    }

    private void CoreWebView2Frame_Destroyed(object? sender, object args)
    {
        if (sender is CoreWebView2Frame frame)
        {
            _frames.Remove(frame);
        }
    }

    private void TrackFrame(CoreWebView2Frame frame)
    {
        _frames.Add(frame);
        frame.FrameCreated += CoreWebView2Frame_FrameCreated;
        frame.Destroyed += CoreWebView2Frame_Destroyed;
    }
    private void ConfigureVisibility(bool visible)
    {
        if (visible)
        {
            ShowInTaskbar = true;
            ShowActivated = true;
            return;
        }

        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStyle = WindowStyle.ToolWindow;
        Width = 2;
        Height = 2;
        Left = -32_000;
        Top = -32_000;
        Opacity = 0.01;
    }

    private void SetStatus(string status)
    {
        PortalStatusTextBlock.Text = status;
    }

    private static string NormalizeSpace(string value)
    {
        return string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed record PortalPageState(
        bool IsReportsPage = false,
        bool IsAuthenticated = false,
        bool HasUsername = false,
        bool HasPassword = false,
        bool HasOneTimePassword = false,
        bool HasCaptcha = false);
}
