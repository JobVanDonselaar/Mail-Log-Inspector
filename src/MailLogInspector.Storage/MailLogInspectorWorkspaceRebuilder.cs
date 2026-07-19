using System.Text.Json;
using MailLogInspector.Core;
using Microsoft.Data.Sqlite;

namespace MailLogInspector.Storage;

public sealed partial class MailLogInspectorWorkspaceRebuilder
{
    private const string SwapJournalFileName = "mail-log-inspector-rebuild.swap.json";
    private readonly MailLogInspectorWorkspacePaths _workspace;

    public MailLogInspectorWorkspaceRebuilder(MailLogInspectorWorkspacePaths workspace)
    {
        _workspace = workspace;
    }

    public async Task<MailLogInspectorWorkspaceRebuildResult> RebuildIfRequiredAsync(
        CancellationToken cancellationToken,
        IProgress<MailLogInspectorImportProgress>? progress = null)
    {
        RecoverInterruptedSwap();

        var currentStore = new MailLogInspectorStore(_workspace.DatabasePath);
        MailLogInspectorDatabaseState currentState = currentStore.GetDatabaseState();
        RecoveryRequest? recoveryRequest = ReadRecoveryRequest();
        if (recoveryRequest != null)
        {
            return await RebuildFromDatabaseAsync(recoveryRequest.SourceDatabasePath, cancellationToken, progress);
        }
        if (currentState == MailLogInspectorDatabaseState.RebuildRequired)
        {
            return await RebuildFromDatabaseAsync(_workspace.DatabasePath, cancellationToken, progress);
        }
        if (currentState == MailLogInspectorDatabaseState.Current)
        {
            currentStore.Initialize();
            DeleteValidatedBackups();
            return new MailLogInspectorWorkspaceRebuildResult(false, null, 0, 0);
        }

        EnsureFreeSpace();
        string rebuildRoot = Path.Combine(_workspace.RootDirectory, ".rebuild-" + Guid.NewGuid().ToString("N"));
        MailLogInspectorWorkspacePaths rebuildWorkspace = MailLogInspectorWorkspaceBootstrapper.Prepare(rebuildRoot);
        int importedFileCount = 0;
        int importedRowCount = 0;

        try
        {
            var rebuildStore = new MailLogInspectorStore(rebuildWorkspace.DatabasePath);
            rebuildStore.Initialize();
            var importer = new MailLogInspectorImportService(rebuildStore, rebuildWorkspace);
            string[] sourceFiles = Directory.EnumerateFiles(_workspace.ArchiveDirectory)
                .Where(IsImportSource)
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (string sourcePath in sourceFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                MailLogInspectorImportResult result = sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                    ? await importer.ImportZipAsync(sourcePath, cancellationToken, progress, finalizeBatch: false, archiveSource: false)
                    : await importer.ImportCsvAsync(sourcePath, cancellationToken, progress, finalizeBatch: false, archiveSource: false);
                importedFileCount++;
                importedRowCount += result.UpsertedCount;
            }

            importer.FinalizeBatch();
            ValidateWorkspace(rebuildWorkspace);
            string? backupPath = SwapValidatedWorkspace(rebuildWorkspace);
            return new MailLogInspectorWorkspaceRebuildResult(true, backupPath, importedFileCount, importedRowCount);
        }
        catch (Exception ex)
        {
            MailLogInspectorLog.Error("rebuild", "Veilige workspace-rebuild mislukt", ex);
            throw;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectoryIfExists(rebuildRoot);
        }
    }

    private string? SwapValidatedWorkspace(MailLogInspectorWorkspacePaths rebuildWorkspace)
    {
        SqliteConnection.ClearAllPools();
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        string? backupDatabasePath = File.Exists(_workspace.DatabasePath)
            ? Path.Combine(_workspace.RootDirectory, $"mail-log-inspector-backup-{timestamp}.sqlite")
            : null;
        string? backupArchiveDirectory = Directory.Exists(_workspace.ArchiveDatabaseDirectory)
            ? Path.Combine(_workspace.RootDirectory, $"ArchiveDb-backup-{timestamp}")
            : null;
        string journalPath = Path.Combine(_workspace.RootDirectory, SwapJournalFileName);
        var journal = new SwapJournal(backupDatabasePath, backupArchiveDirectory);
        File.WriteAllText(journalPath, JsonSerializer.Serialize(journal));

        try
        {
            if (backupDatabasePath != null)
            {
                MoveDatabaseWithSidecars(_workspace.DatabasePath, backupDatabasePath);
            }

            if (backupArchiveDirectory != null)
            {
                Directory.Move(_workspace.ArchiveDatabaseDirectory, backupArchiveDirectory);
            }

            MoveDatabaseWithSidecars(rebuildWorkspace.DatabasePath, _workspace.DatabasePath);
            Directory.Move(rebuildWorkspace.ArchiveDatabaseDirectory, _workspace.ArchiveDatabaseDirectory);
            ValidateWorkspace(_workspace);
            File.Delete(journalPath);
            return backupDatabasePath;
        }
        catch
        {
            RestorePreviousWorkspace(journal);
            throw;
        }
    }

    private void RecoverInterruptedSwap()
    {
        string journalPath = Path.Combine(_workspace.RootDirectory, SwapJournalFileName);
        if (!File.Exists(journalPath))
        {
            return;
        }

        SwapJournal? journal = JsonSerializer.Deserialize<SwapJournal>(File.ReadAllText(journalPath));
        if (journal == null)
        {
            throw new InvalidDataException("Het database-wisseljournal is ongeldig.");
        }

        SqliteConnection.ClearAllPools();
        RestorePreviousWorkspace(journal);
        File.Delete(journalPath);
    }

    private void RestorePreviousWorkspace(SwapJournal journal)
    {
        SqliteConnection.ClearAllPools();
        if (!string.IsNullOrWhiteSpace(journal.BackupDatabasePath) && File.Exists(journal.BackupDatabasePath))
        {
            DeleteDatabaseWithSidecars(_workspace.DatabasePath);
            MoveDatabaseWithSidecars(journal.BackupDatabasePath, _workspace.DatabasePath);
        }

        if (!string.IsNullOrWhiteSpace(journal.BackupArchiveDirectory) && Directory.Exists(journal.BackupArchiveDirectory))
        {
            DeleteDirectoryIfExists(_workspace.ArchiveDatabaseDirectory);
            Directory.Move(journal.BackupArchiveDirectory, _workspace.ArchiveDatabaseDirectory);
        }
    }

    private static void ValidateWorkspace(MailLogInspectorWorkspacePaths workspace)
    {
        ValidateDatabase(workspace.DatabasePath);
        foreach (string archiveDatabase in Directory.EnumerateFiles(workspace.ArchiveDatabaseDirectory, "*.sqlite"))
        {
            ValidateDatabase(archiveDatabase);
        }
    }

    private static void ValidateDatabase(string databasePath)
    {
        var store = new MailLogInspectorStore(databasePath);
        if (store.GetDatabaseState() != MailLogInspectorDatabaseState.Current)
        {
            throw new InvalidDataException($"Database heeft niet het actuele schema: {databasePath}");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            DefaultTimeout = 30
        }.ToString());
        connection.Open();
        using SqliteCommand integrityCommand = connection.CreateCommand();
        integrityCommand.CommandText = "PRAGMA quick_check;";
        if (!string.Equals(Convert.ToString(integrityCommand.ExecuteScalar()), "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"SQLite-integriteitscontrole mislukt: {databasePath}");
        }

        using SqliteCommand aggregateCommand = connection.CreateCommand();
        aggregateCommand.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM mail_items WHERE accepted_at IS NOT NULL),
                COALESCE((SELECT SUM(total) FROM analysis_daily_status), 0);
            """;
        using SqliteDataReader reader = aggregateCommand.ExecuteReader();
        if (!reader.Read() || reader.GetInt64(0) != reader.GetInt64(1))
        {
            throw new InvalidDataException($"Analyse-totalen komen niet overeen met de detailregels: {databasePath}");
        }
    }

    private void DeleteValidatedBackups()
    {
        foreach (string backupDatabase in Directory.EnumerateFiles(_workspace.RootDirectory, "mail-log-inspector-backup-*.sqlite"))
        {
            DeleteDatabaseWithSidecars(backupDatabase);
        }
        foreach (string backupArchiveDirectory in Directory.EnumerateDirectories(_workspace.RootDirectory, "ArchiveDb-backup-*"))
        {
            DeleteDirectoryIfExists(backupArchiveDirectory);
        }
    }
    private void EnsureFreeSpace()
    {
        long existingDatabaseBytes = File.Exists(_workspace.DatabasePath) ? new FileInfo(_workspace.DatabasePath).Length : 0;
        if (Directory.Exists(_workspace.ArchiveDatabaseDirectory))
        {
            existingDatabaseBytes += Directory.EnumerateFiles(_workspace.ArchiveDatabaseDirectory, "*.sqlite")
                .Sum(static path => new FileInfo(path).Length);
        }

        string? root = Path.GetPathRoot(_workspace.RootDirectory);
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        long requiredBytes = existingDatabaseBytes + 2L * 1024 * 1024 * 1024;
        if (new DriveInfo(root).AvailableFreeSpace < requiredBytes)
        {
            throw new IOException($"Onvoldoende vrije schijfruimte voor veilige database-opbouw. Benodigd: {requiredBytes:n0} bytes.");
        }
    }

    private static bool IsImportSource(string path)
    {
        return path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    }

    private static void MoveDatabaseWithSidecars(string sourcePath, string destinationPath)
    {
        MoveIfExists(sourcePath, destinationPath);
        MoveIfExists(sourcePath + "-wal", destinationPath + "-wal");
        MoveIfExists(sourcePath + "-shm", destinationPath + "-shm");
    }

    private static void DeleteDatabaseWithSidecars(string databasePath)
    {
        DeleteFileIfExists(databasePath);
        DeleteFileIfExists(databasePath + "-wal");
        DeleteFileIfExists(databasePath + "-shm");
    }

    private static void MoveIfExists(string sourcePath, string destinationPath)
    {
        if (File.Exists(sourcePath))
        {
            File.Move(sourcePath, destinationPath, true);
        }
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }

    private sealed record SwapJournal(string? BackupDatabasePath, string? BackupArchiveDirectory);
}