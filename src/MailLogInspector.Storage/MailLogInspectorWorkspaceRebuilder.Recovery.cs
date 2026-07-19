using System.Text.Json;
using MailLogInspector.Core;
using Microsoft.Data.Sqlite;

namespace MailLogInspector.Storage;

public sealed partial class MailLogInspectorWorkspaceRebuilder
{
    private const string RecoveryRequestFileName = "mail-log-inspector-recovery.request.json";

    public void RequestRecoveryFrom(string sourceDatabasePath)
    {
        string fullSourcePath = Path.GetFullPath(sourceDatabasePath);
        string workspaceRoot = Path.GetFullPath(_workspace.RootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullSourcePath.StartsWith(workspaceRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("De recovery-database moet binnen de Mail Log Inspector-workspace staan.");
        }
        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException("De recovery-database bestaat niet.", fullSourcePath);
        }

        File.WriteAllText(
            Path.Combine(_workspace.RootDirectory, RecoveryRequestFileName),
            JsonSerializer.Serialize(new RecoveryRequest(fullSourcePath)));
    }

    private RecoveryRequest? ReadRecoveryRequest()
    {
        string requestPath = Path.Combine(_workspace.RootDirectory, RecoveryRequestFileName);
        if (!File.Exists(requestPath))
        {
            return null;
        }

        RecoveryRequest? request = JsonSerializer.Deserialize<RecoveryRequest>(File.ReadAllText(requestPath));
        if (request == null || !File.Exists(request.SourceDatabasePath))
        {
            throw new InvalidDataException("Het database-recoveryverzoek is ongeldig of de backup ontbreekt.");
        }
        return request;
    }

    private async Task<MailLogInspectorWorkspaceRebuildResult> RebuildFromDatabaseAsync(
        string sourceDatabasePath,
        CancellationToken cancellationToken,
        IProgress<MailLogInspectorImportProgress>? progress)
    {
        EnsureFreeSpace();
        string rebuildRoot = Path.Combine(_workspace.RootDirectory, ".rebuild-" + Guid.NewGuid().ToString("N"));
        MailLogInspectorWorkspacePaths rebuildWorkspace = MailLogInspectorWorkspaceBootstrapper.Prepare(rebuildRoot);
        string sourceArchiveDirectory = ResolveSourceArchiveDirectory(sourceDatabasePath);

        try
        {
            progress?.Report(new MailLogInspectorImportProgress(
                MailLogInspectorImportStage.Preparing,
                "Bestaande database veilig migreren...",
                0,
                0,
                new FileInfo(sourceDatabasePath).Length,
                0));

            WorkspaceCounts sourceCounts = ReadWorkspaceCounts(sourceDatabasePath, sourceArchiveDirectory);
            WorkspaceCounts targetCounts = await Task.Run(
                () => MigrateWorkspace(sourceDatabasePath, sourceArchiveDirectory, rebuildWorkspace, cancellationToken),
                cancellationToken);
            if (sourceCounts != targetCounts)
            {
                throw new InvalidDataException(
                    $"Migratie-aantallen wijken af. Bron: {sourceCounts.Rows:n0} regels/{sourceCounts.Imports:n0} imports; " +
                    $"doel: {targetCounts.Rows:n0} regels/{targetCounts.Imports:n0} imports.");
            }

            ValidateWorkspace(rebuildWorkspace);
            string? backupPath = SwapValidatedWorkspace(rebuildWorkspace);
            DeleteRecoveryRequest();
            progress?.Report(new MailLogInspectorImportProgress(
                MailLogInspectorImportStage.Completed,
                "Database veilig gemigreerd.",
                100,
                new FileInfo(sourceDatabasePath).Length,
                new FileInfo(sourceDatabasePath).Length,
                targetCounts.Rows));
            return new MailLogInspectorWorkspaceRebuildResult(
                true,
                backupPath,
                targetCounts.Imports,
                targetCounts.Rows,
                sourceDatabasePath);
        }
        catch (Exception ex)
        {
            MailLogInspectorLog.Error("rebuild", "Veilige database-migratie mislukt", ex);
            throw;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectoryIfExists(rebuildRoot);
        }
    }

    private static WorkspaceCounts MigrateWorkspace(
        string sourceDatabasePath,
        string sourceArchiveDirectory,
        MailLogInspectorWorkspacePaths targetWorkspace,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LegacyDatabaseMigrationResult active = MailLogInspectorLegacyDatabaseMigrator.Migrate(
            sourceDatabasePath,
            targetWorkspace.DatabasePath);
        var total = new WorkspaceCounts(active.RowCount, active.ImportCount);

        if (!Directory.Exists(sourceArchiveDirectory))
        {
            return total;
        }

        foreach (string sourceArchive in Directory.EnumerateFiles(sourceArchiveDirectory, "*.sqlite").OrderBy(Path.GetFileName))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string targetArchive = Path.Combine(targetWorkspace.ArchiveDatabaseDirectory, Path.GetFileName(sourceArchive));
            LegacyDatabaseMigrationResult archive = MailLogInspectorLegacyDatabaseMigrator.Migrate(sourceArchive, targetArchive);
            total = new WorkspaceCounts(total.Rows + archive.RowCount, total.Imports + archive.ImportCount);
        }

        return total;
    }

    private static WorkspaceCounts ReadWorkspaceCounts(string databasePath, string archiveDirectory)
    {
        LegacyDatabaseMigrationResult active = MailLogInspectorLegacyDatabaseMigrator.ReadCounts(databasePath);
        var total = new WorkspaceCounts(active.RowCount, active.ImportCount);
        if (!Directory.Exists(archiveDirectory))
        {
            return total;
        }

        foreach (string archivePath in Directory.EnumerateFiles(archiveDirectory, "*.sqlite"))
        {
            LegacyDatabaseMigrationResult archive = MailLogInspectorLegacyDatabaseMigrator.ReadCounts(archivePath);
            total = new WorkspaceCounts(total.Rows + archive.RowCount, total.Imports + archive.ImportCount);
        }
        return total;
    }

    private string ResolveSourceArchiveDirectory(string sourceDatabasePath)
    {
        if (string.Equals(
                Path.GetFullPath(sourceDatabasePath),
                Path.GetFullPath(_workspace.DatabasePath),
                StringComparison.OrdinalIgnoreCase))
        {
            return _workspace.ArchiveDatabaseDirectory;
        }

        return Path.Combine(Path.GetDirectoryName(sourceDatabasePath)!, "ArchiveDb");
    }

    private void DeleteRecoveryRequest()
    {
        string requestPath = Path.Combine(_workspace.RootDirectory, RecoveryRequestFileName);
        if (File.Exists(requestPath))
        {
            File.Delete(requestPath);
        }
    }

    private sealed record RecoveryRequest(string SourceDatabasePath);
    private readonly record struct WorkspaceCounts(int Rows, int Imports);
}
