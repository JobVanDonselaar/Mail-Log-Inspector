using MailLogInspector.Core;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class MailLogInspectorWorkspaceBootstrapperTests
{
	[Fact]
	public void ResolveWorkspaceRoot_PrefersCurrentDirectoryWhenShortcutProvidesStartInFolder()
	{
		string executableBaseDirectory = Path.Combine(Path.GetTempPath(), "MailLogInspectorTests", Guid.NewGuid().ToString("N"), "bin");
		string currentDirectory = Path.Combine(Path.GetTempPath(), "MailLogInspectorTests", Guid.NewGuid().ToString("N"), "workspace");
		Directory.CreateDirectory(executableBaseDirectory);
		Directory.CreateDirectory(currentDirectory);

		string resolved = MailLogInspectorWorkspaceBootstrapper.ResolveWorkspaceRoot(null, executableBaseDirectory, currentDirectory);

		Assert.Equal(currentDirectory, resolved);
	}

	[Fact]
	public void ResolveWorkspaceRoot_FallsBackToExecutableDirectoryWhenCurrentDirectoryDoesNotExist()
	{
		string executableBaseDirectory = Path.Combine(Path.GetTempPath(), "MailLogInspectorTests", Guid.NewGuid().ToString("N"), "bin");
		string currentDirectory = Path.Combine(Path.GetTempPath(), "MailLogInspectorTests", Guid.NewGuid().ToString("N"), "missing");
		Directory.CreateDirectory(executableBaseDirectory);

		string resolved = MailLogInspectorWorkspaceBootstrapper.ResolveWorkspaceRoot(null, executableBaseDirectory, currentDirectory);

		Assert.Equal(executableBaseDirectory, resolved);
	}

	[Fact]
	public void ResolveWorkspaceRoot_FallsBackToInstalledRootWhenCandidatesAreNetworkPaths()
	{
		string executableBaseDirectory = @"Z:\Mail Log Inspector";
		string currentDirectory = @"\\VBoxSvr\Apps\Mail Log Inspector";
		string installedRoot = Path.Combine(Path.GetTempPath(), "MailLogInspectorTests", Guid.NewGuid().ToString("N"), "installed-root");
		string localAppDataRoot = Path.Combine(Path.GetTempPath(), "MailLogInspectorTests", Guid.NewGuid().ToString("N"), "local-root");
		Directory.CreateDirectory(installedRoot);
		Directory.CreateDirectory(localAppDataRoot);

		string resolved = MailLogInspectorWorkspaceBootstrapper.ResolveWorkspaceRoot(null, executableBaseDirectory, currentDirectory, installedRoot, localAppDataRoot);

		Assert.Equal(installedRoot, resolved);
	}

	[Fact]
	public void ResolveWorkspaceRoot_FallsBackToLocalRootWhenInstalledRootMissingAndCandidatesAreNetworkPaths()
	{
		string executableBaseDirectory = @"Z:\Mail Log Inspector";
		string currentDirectory = @"\\VBoxSvr\Apps\Mail Log Inspector";
		string installedRoot = Path.Combine(Path.GetTempPath(), "MailLogInspectorTests", Guid.NewGuid().ToString("N"), "missing-installed-root");
		string localAppDataRoot = Path.Combine(Path.GetTempPath(), "MailLogInspectorTests", Guid.NewGuid().ToString("N"), "local-root");
		Directory.CreateDirectory(localAppDataRoot);

		string resolved = MailLogInspectorWorkspaceBootstrapper.ResolveWorkspaceRoot(null, executableBaseDirectory, currentDirectory, installedRoot, localAppDataRoot);

		Assert.Equal(localAppDataRoot, resolved);
	}
}
