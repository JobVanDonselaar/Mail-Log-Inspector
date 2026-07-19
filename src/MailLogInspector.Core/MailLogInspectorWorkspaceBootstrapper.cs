using System;
using System.IO;

namespace MailLogInspector.Core;

public static class MailLogInspectorWorkspaceBootstrapper
{
	public const string DatabaseFileName = "mail-log-inspector.sqlite";
	private const string PreferredInstalledRoot = @"C:\Apps\Mail Log Inspector";

	public static MailLogInspectorWorkspacePaths Prepare(string? baseDirectory = null)
	{
		string fullPath = Path.GetFullPath(ResolveWorkspaceRoot(baseDirectory, AppContext.BaseDirectory, Environment.CurrentDirectory));
		string databasePath = Path.Combine(fullPath, "mail-log-inspector.sqlite");
		string text = Path.Combine(fullPath, "Archive");
		string text2 = Path.Combine(fullPath, "Incoming");
		string text3 = Path.Combine(fullPath, "ArchiveDb");
		string text4 = Path.Combine(fullPath, "mail-log-inspector-settings.sqlite");
		string text5 = Path.Combine(text2, "SmtpReports");
		Directory.CreateDirectory(fullPath);
		Directory.CreateDirectory(text);
		Directory.CreateDirectory(text2);
		Directory.CreateDirectory(text3);
		Directory.CreateDirectory(text5);
		return new MailLogInspectorWorkspacePaths(fullPath, databasePath, text, text2, text3, text4, text5);
	}

	public static string ResolveWorkspaceRoot(string? requestedBaseDirectory, string executableBaseDirectory, string currentDirectory)
	{
		return ResolveWorkspaceRoot(
			requestedBaseDirectory,
			executableBaseDirectory,
			currentDirectory,
			PreferredInstalledRoot,
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mail Log Inspector"));
	}

	internal static string ResolveWorkspaceRoot(
		string? requestedBaseDirectory,
		string executableBaseDirectory,
		string currentDirectory,
		string preferredInstalledRoot,
		string localAppDataRoot)
	{
		if (!string.IsNullOrWhiteSpace(requestedBaseDirectory))
		{
			return requestedBaseDirectory;
		}

		if (IsStableLocalWorkspaceRoot(currentDirectory))
		{
			return currentDirectory;
		}

		if (IsStableLocalWorkspaceRoot(executableBaseDirectory))
		{
			return executableBaseDirectory;
		}

		if (!string.IsNullOrWhiteSpace(preferredInstalledRoot) && Directory.Exists(preferredInstalledRoot))
		{
			return preferredInstalledRoot;
		}

		return localAppDataRoot;
	}

	private static bool IsStableLocalWorkspaceRoot(string? path)
	{
		if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
		{
			return false;
		}

		string fullPath = Path.GetFullPath(path);
		if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
		{
			return false;
		}

		string? root = Path.GetPathRoot(fullPath);
		if (string.IsNullOrWhiteSpace(root))
		{
			return false;
		}

		try
		{
			DriveInfo drive = new DriveInfo(root);
			return drive.DriveType == DriveType.Fixed;
		}
		catch
		{
			return false;
		}
	}
}
