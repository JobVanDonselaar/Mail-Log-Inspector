using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace MailLogInspector.App;

public partial class MainWindow
{	private void InitializeTrayIcon()
	{
		Forms.ContextMenuStrip menu = new();
		menu.Items.Add("Openen", null, (_, _) => ((DispatcherObject)this).Dispatcher.Invoke(RestoreFromExternalActivation));
		menu.Items.Add("Afsluiten", null, (_, _) => ((DispatcherObject)this).Dispatcher.Invoke(ExitFromTray));

		_notifyIcon = new Forms.NotifyIcon
		{
			Text = "Mail Log Inspector",
			Icon = ResolveTrayIcon(),
			ContextMenuStrip = menu,
			Visible = false
		};
		_notifyIcon.DoubleClick += (_, _) => ((DispatcherObject)this).Dispatcher.Invoke(RestoreFromExternalActivation);
	}

	private static Drawing.Icon ResolveTrayIcon()
	{
		try
		{
			string? executablePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
			if (!string.IsNullOrWhiteSpace(executablePath))
			{
				Drawing.Icon? icon = Drawing.Icon.ExtractAssociatedIcon(executablePath);
				if (icon is not null)
				{
					return icon;
				}
			}
		}
		catch
		{
			// Fall back to a safe system icon when the executable icon cannot be read.
		}

		return Drawing.SystemIcons.Application;
	}

	private void MainWindow_Closing(object? sender, CancelEventArgs e)
	{
		if (_allowCloseFromTray)
		{
			return;
		}

		if (!_closeToTrayEnabled)
		{
			DisposeTrayIcon();
			return;
		}

		e.Cancel = true;
		HideToTray();
	}

	private void HideToTray()
	{
		if (_notifyIcon is not null)
		{
			_notifyIcon.Visible = true;
		}

		Hide();
	}

	internal void RestoreFromExternalActivation()
	{
		Show();
		if (WindowState == WindowState.Minimized)
		{
			WindowState = WindowState.Normal;
		}

		Activate();
		Topmost = true;
		Topmost = false;
		Focus();
		if (_notifyIcon is not null)
		{
			_notifyIcon.Visible = false;
		}
	}

	private void ExitFromTray()
	{
		_allowCloseFromTray = true;
		if (_notifyIcon is not null)
		{
			_notifyIcon.Visible = false;
		}

		Close();
	}

	private void DisposeTrayIcon()
	{
		if (_notifyIcon is null)
		{
			return;
		}

		_notifyIcon.Visible = false;
		_notifyIcon.Dispose();
		_notifyIcon = null;
	}
}
