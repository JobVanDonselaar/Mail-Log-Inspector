using System.IO.Compression;
using MailLogInspector.Core;
using MailLogInspector.Storage;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class MailLogInspectorImportValidationTests
{
    private const string Headers =
        "Date accepted,Date delivered,Mail from,Recipient,Status,Response code," +
        "Response message,Bounce class,Tries,Sender id,Tracking id,Campaign id";

    [Fact]
    public void ImportLimits_AllowThreeGigabyteCsvAndRetainZipLimit()
    {
        Assert.Equal(3L * 1024 * 1024 * 1024, MailLogInspectorImportLimits.MaxCsvBytes);
        Assert.Equal(512L * 1024 * 1024, MailLogInspectorImportLimits.MaxZipBytes);
    }

    [Fact]
    public async Task ImportZipAsync_RejectsFileWhenEveryDataRowIsInvalid()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "mail-log-invalid-import-" + Guid.NewGuid().ToString("N"));
        MailLogInspectorWorkspacePaths workspace = MailLogInspectorWorkspaceBootstrapper.Prepare(root);
        var store = new MailLogInspectorStore(workspace.DatabasePath);
        store.Initialize();
        string zipPath = CreateZip(root, Headers + Environment.NewLine +
            "not-a-date,,sender@example.com,target@example.net,D,250,ok,,1,,track-1,");
        var importer = new MailLogInspectorImportService(store, workspace);

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(
            () => importer.ImportZipAsync(zipPath, CancellationToken.None));

        Assert.Contains("Geen geldige mailregels", error.Message, StringComparison.Ordinal);
        Assert.Empty(store.ReadRecentImports());
    }

    [Fact]
    public async Task ImportZipAsync_AcceptsHeaderOnlyDailyReport()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "mail-log-zero-report-" + Guid.NewGuid().ToString("N"));
        MailLogInspectorWorkspacePaths workspace = MailLogInspectorWorkspaceBootstrapper.Prepare(root);
        var store = new MailLogInspectorStore(workspace.DatabasePath);
        store.Initialize();
        string zipPath = CreateZip(root, Headers);
        var importer = new MailLogInspectorImportService(store, workspace);

        MailLogInspectorImportResult result =
            await importer.ImportZipAsync(zipPath, CancellationToken.None);

        Assert.False(result.AlreadyImported);
        Assert.Equal(0, result.SourceRowCount);
        Assert.Equal(0, result.ErrorCount);
        Assert.Single(store.ReadRecentImports());
    }

    private static string CreateZip(string root, string csv)
    {
        string path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
        using FileStream stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        using StreamWriter writer = new(archive.CreateEntry("report.csv").Open());
        writer.Write(csv);
        return path;
    }
}
