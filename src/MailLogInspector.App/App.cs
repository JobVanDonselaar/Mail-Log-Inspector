using System;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Markup;
using MailLogInspector.Core;
using MailLogInspector.Storage;

namespace MailLogInspector.App;

public class App : System.Windows.Application
{
    private SingleInstanceCoordinator? _singleInstanceCoordinator;
    private AdminSettingsWindow? _adminSettingsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        ApplyDisplayCulture();
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        AppActivationRequest startupRequest = AppStartupRequestParser.Parse(e.Args);

        _singleInstanceCoordinator = new SingleInstanceCoordinator();
        if (!_singleInstanceCoordinator.TryAcquirePrimaryInstance())
        {
            SingleInstanceCoordinator.TrySignalExistingInstance(startupRequest);
            _singleInstanceCoordinator.Dispose();
            _singleInstanceCoordinator = null;
            Shutdown(0);
            return;
        }

        _singleInstanceCoordinator.StartActivationListener(RequestExternalActivation);

        base.OnStartup(e);
        try
        {
            if (startupRequest == AppActivationRequest.Admin && !ShowAdminSettings(owner: null))
            {
                Shutdown(0);
                return;
            }

            MainWindow mainWindow = (MainWindow)(MainWindow = new MainWindow());
            mainWindow.Show();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
        catch (Exception ex)
        {
            MailLogInspectorLog.Error("startup", "Applicatie kon niet starten", ex);
            System.Windows.MessageBox.Show(ex.Message, "Mail Log Inspector", MessageBoxButton.OK, MessageBoxImage.Hand);
            Shutdown(-1);
        }
    }

    public bool ShowAdminSettings(Window? owner)
    {
        if (_adminSettingsWindow is not null)
        {
            _adminSettingsWindow.Activate();
            return false;
        }

        MailLogInspectorWorkspacePaths workspace = MailLogInspectorWorkspaceBootstrapper.Prepare();
        MailLogInspectorLog.Configure(workspace.RootDirectory);
        var store = new GmailReportOperationalStore(workspace.GmailOperationalDatabasePath);
        store.Initialize();
        GmailOAuthService.MigrateLegacyClientSecret(store);
        var smtpPortalStore = new SmtpPortalOperationalStore(workspace.GmailOperationalDatabasePath);
        smtpPortalStore.Initialize();
        var syncStore = new ReportSyncOperationalStore(workspace.GmailOperationalDatabasePath);
        syncStore.Initialize();
        var window = new AdminSettingsWindow(store, smtpPortalStore, syncStore, workspace);
        if (owner is not null)
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        _adminSettingsWindow = window;
        try
        {
            return window.ShowDialog() == true;
        }
        finally
        {
            _adminSettingsWindow = null;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceCoordinator?.Dispose();
        _singleInstanceCoordinator = null;
        base.OnExit(e);
    }

    private static void ApplyDisplayCulture()
    {
        CultureInfo.CurrentCulture = MailLogInspectorDisplayFormats.Culture;
        CultureInfo.CurrentUICulture = MailLogInspectorDisplayFormats.Culture;
        CultureInfo.DefaultThreadCurrentCulture = MailLogInspectorDisplayFormats.Culture;
        CultureInfo.DefaultThreadCurrentUICulture = MailLogInspectorDisplayFormats.Culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(MailLogInspectorDisplayFormats.Culture.IetfLanguageTag)));
    }

    private void RequestExternalActivation(AppActivationRequest request)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (MainWindow is not MainWindow mainWindow)
            {
                return;
            }

            mainWindow.RestoreFromExternalActivation();
            if (request == AppActivationRequest.Admin)
            {
                ShowAdminSettings(mainWindow);
            }
        }));
    }

    [STAThread]
    public static void Main()
    {
        new App().Run();
    }
}

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private const string MutexName = "Local\\MailLogInspector.SingleInstance";
    private const string PipeName = "MailLogInspector.SingleInstance.Activate";
    private const int PipeConnectTimeoutMilliseconds = 900;

    private readonly Mutex _mutex = new(false, MutexName);
    private CancellationTokenSource? _listenerCancellation;
    private Task? _listenerTask;
    private bool _ownsMutex;
    private bool _disposed;

    public bool TryAcquirePrimaryInstance()
    {
        try
        {
            _ownsMutex = _mutex.WaitOne(0);
            return _ownsMutex;
        }
        catch (AbandonedMutexException)
        {
            _ownsMutex = true;
            return true;
        }
    }

    public static bool TrySignalExistingInstance(AppActivationRequest request)
    {
        try
        {
            using NamedPipeClientStream client = new(".", PipeName, PipeDirection.Out);
            client.Connect(PipeConnectTimeoutMilliseconds);
            using StreamWriter writer = new(client) { AutoFlush = true };
            string requestValue = AppStartupRequestParser.ToPipeValue(request);
            writer.WriteLine(requestValue);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void StartActivationListener(Action<AppActivationRequest> handleRequest)
    {
        if (!_ownsMutex)
        {
            throw new InvalidOperationException("Activation listener can only run in the primary Mail Log Inspector instance.");
        }

        _listenerCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _listenerCancellation.Token;
        _listenerTask = Task.Run(() => ListenForActivationRequestsAsync(handleRequest, cancellationToken), CancellationToken.None);
    }

    private static async Task ListenForActivationRequestsAsync(
        Action<AppActivationRequest> handleRequest,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using NamedPipeServerStream server = new(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using StreamReader reader = new(server);
                string? requestValue = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                handleRequest(AppStartupRequestParser.ParsePipeValue(requestValue));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
                // Broken client pipes are harmless; keep the primary instance listening.
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _listenerCancellation?.Cancel();
        if (_listenerTask is not null)
        {
            try
            {
                _listenerTask.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }
        }
        _listenerTask = null;
        _listenerCancellation?.Dispose();
        _listenerCancellation = null;

        if (_ownsMutex)
        {
            _mutex.ReleaseMutex();
            _ownsMutex = false;
        }

        _mutex.Dispose();
    }
}
