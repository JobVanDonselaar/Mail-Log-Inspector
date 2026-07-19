using MailLogInspector.Core;
using MailLogInspector.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class MailLogInspectorWorkspaceRebuilderTests
{
    [Fact]
    public async Task RebuildIfRequiredAsync_DerivesMissingLegacyImportStatusCounts()
    {
        string root = Path.Combine(Path.GetTempPath(), "workspace-rebuild-oldest-legacy-" + Guid.NewGuid().ToString("N"));
        MailLogInspectorWorkspacePaths workspace = MailLogInspectorWorkspaceBootstrapper.Prepare(root);
        CreateLegacyDatabase(workspace.DatabasePath, 3);
        using (var connection = new SqliteConnection($"Data Source={workspace.DatabasePath}"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE imports DROP COLUMN delivered_count; ALTER TABLE imports DROP COLUMN bounce_count; ALTER TABLE imports DROP COLUMN underway_count;";
            command.ExecuteNonQuery();
        }

        var rebuilder = new MailLogInspectorWorkspaceRebuilder(workspace);
        MailLogInspectorWorkspaceRebuildResult result = await rebuilder.RebuildIfRequiredAsync(CancellationToken.None);

        Assert.True(result.WasRebuilt);
        using var rebuilt = new SqliteConnection($"Data Source={workspace.DatabasePath};Mode=ReadOnly");
        rebuilt.Open();
        using SqliteCommand read = rebuilt.CreateCommand();
        read.CommandText = "SELECT delivered_count, bounce_count, underway_count FROM imports WHERE import_id = 1;";
        using SqliteDataReader reader = read.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(3, reader.GetInt32(0));
        Assert.Equal(0, reader.GetInt32(1));
        Assert.Equal(0, reader.GetInt32(2));
    }
    [Fact]
    public async Task RebuildIfRequiredAsync_MigratesAllLegacyRowsWithoutArchiveSources()
    {
        string root = Path.Combine(Path.GetTempPath(), "workspace-rebuild-legacy-" + Guid.NewGuid().ToString("N"));
        MailLogInspectorWorkspacePaths workspace = MailLogInspectorWorkspaceBootstrapper.Prepare(root);
        CreateLegacyDatabase(workspace.DatabasePath, 3);
        var rebuilder = new MailLogInspectorWorkspaceRebuilder(workspace);

        MailLogInspectorWorkspaceRebuildResult result = await rebuilder.RebuildIfRequiredAsync(CancellationToken.None);

        Assert.True(result.WasRebuilt);
        Assert.Equal(3, result.ImportedRowCount);
        Assert.Equal(3, new MailLogInspectorStore(workspace.DatabasePath).CountMailItems());
    }

    [Fact]
    public async Task RebuildIfRequiredAsync_UsesRequestedRecoveryDatabaseWhenActiveDatabaseIsCurrent()
    {
        string root = Path.Combine(Path.GetTempPath(), "workspace-rebuild-recovery-" + Guid.NewGuid().ToString("N"));
        MailLogInspectorWorkspacePaths workspace = MailLogInspectorWorkspaceBootstrapper.Prepare(root);
        var currentStore = new MailLogInspectorStore(workspace.DatabasePath);
        currentStore.Initialize();
        string backupDirectory = Path.Combine(root, "Backups", "pre-publish");
        Directory.CreateDirectory(backupDirectory);
        string backupPath = Path.Combine(backupDirectory, "mail-log-inspector.sqlite");
        CreateLegacyDatabase(backupPath, 4);
        var rebuilder = new MailLogInspectorWorkspaceRebuilder(workspace);
        rebuilder.RequestRecoveryFrom(backupPath);

        MailLogInspectorWorkspaceRebuildResult result = await rebuilder.RebuildIfRequiredAsync(CancellationToken.None);

        Assert.True(result.WasRebuilt);
        Assert.Equal(4, result.ImportedRowCount);
        Assert.Equal(backupPath, result.GetType().GetProperty("SourceDatabasePath")?.GetValue(result));
        Assert.Equal(4, new MailLogInspectorStore(workspace.DatabasePath).CountMailItems());
    }
    [Fact]
    public async Task RebuildIfRequiredAsync_LeavesActiveDatabaseUntouched_WhenRebuildFails()
    {
        string root = Path.Combine(Path.GetTempPath(), "workspace-rebuild-" + Guid.NewGuid().ToString("N"));
        MailLogInspectorWorkspacePaths workspace = MailLogInspectorWorkspaceBootstrapper.Prepare(root);
        using (var connection = new SqliteConnection($"Data Source={workspace.DatabasePath}"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE mail_items (tracking_id TEXT NOT NULL);";
            command.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
        byte[] expectedDatabase = File.ReadAllBytes(workspace.DatabasePath);
        File.WriteAllText(Path.Combine(workspace.ArchiveDirectory, "broken.zip"), "not a zip");
        var rebuilder = new MailLogInspectorWorkspaceRebuilder(workspace);

        await Assert.ThrowsAnyAsync<Exception>(() => rebuilder.RebuildIfRequiredAsync(CancellationToken.None));
        SqliteConnection.ClearAllPools();

        Assert.True(File.Exists(workspace.DatabasePath));
        Assert.Equal(expectedDatabase, File.ReadAllBytes(workspace.DatabasePath));
    }

    [Fact]
    public async Task RebuildIfRequiredAsync_SwapsOnlyAfterValidatedRebuild()
    {
        string root = Path.Combine(Path.GetTempPath(), "workspace-rebuild-success-" + Guid.NewGuid().ToString("N"));
        MailLogInspectorWorkspacePaths workspace = MailLogInspectorWorkspaceBootstrapper.Prepare(root);
        CreateLegacyDatabase(workspace.DatabasePath, 1);
        var rebuilder = new MailLogInspectorWorkspaceRebuilder(workspace);

        MailLogInspectorWorkspaceRebuildResult result = await rebuilder.RebuildIfRequiredAsync(CancellationToken.None);

        Assert.True(result.WasRebuilt);
        Assert.NotNull(result.BackupDatabasePath);
        Assert.True(File.Exists(result.BackupDatabasePath));
        var rebuiltStore = new MailLogInspectorStore(workspace.DatabasePath);
        Assert.Equal(MailLogInspectorDatabaseState.Current, rebuiltStore.GetDatabaseState());
        Assert.Equal(1, rebuiltStore.CountMailItems());

        await rebuilder.RebuildIfRequiredAsync(CancellationToken.None);

        Assert.False(File.Exists(result.BackupDatabasePath));
    }

    [Fact]
    public async Task RebuildIfRequiredAsync_DoesNotRunFullArchiveValidationForCurrentWorkspace()
    {
        string root = Path.Combine(Path.GetTempPath(), "workspace-fast-start-" + Guid.NewGuid().ToString("N"));
        MailLogInspectorWorkspacePaths workspace = MailLogInspectorWorkspaceBootstrapper.Prepare(root);
        new MailLogInspectorStore(workspace.DatabasePath).Initialize();
        File.WriteAllText(Path.Combine(workspace.ArchiveDatabaseDirectory, "2026-01.sqlite"), "not a sqlite database");
        var rebuilder = new MailLogInspectorWorkspaceRebuilder(workspace);

        MailLogInspectorWorkspaceRebuildResult result = await rebuilder.RebuildIfRequiredAsync(CancellationToken.None);

        Assert.False(result.WasRebuilt);
    }
    private static void CreateLegacyDatabase(string databasePath, int rowCount)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE imports (
                import_id INTEGER PRIMARY KEY AUTOINCREMENT,
                source_path TEXT NOT NULL,
                source_file_name TEXT NOT NULL,
                source_hash TEXT NOT NULL,
                imported_at TEXT NOT NULL,
                report_start TEXT NULL,
                report_end TEXT NULL,
                row_count INTEGER NOT NULL,
                delivered_count INTEGER NOT NULL DEFAULT 0,
                bounce_count INTEGER NOT NULL DEFAULT 0,
                underway_count INTEGER NOT NULL DEFAULT 0,
                archive_path TEXT NULL
            );
            CREATE TABLE mail_domains (
                domain_id INTEGER PRIMARY KEY AUTOINCREMENT,
                domain_name TEXT NOT NULL
            );
            CREATE TABLE mail_addresses (
                address_id INTEGER PRIMARY KEY AUTOINCREMENT,
                local_part TEXT NOT NULL,
                domain_id INTEGER NULL
            );
            CREATE TABLE mail_items (
                tracking_key BLOB NOT NULL,
                recipient_address_id INTEGER NOT NULL,
                recipient_domain_id INTEGER NULL,
                sender_address_id INTEGER NOT NULL,
                sender_domain_id INTEGER NULL,
                accepted_at INTEGER NULL,
                status INTEGER NOT NULL,
                last_seen_at INTEGER NOT NULL,
                duration_seconds INTEGER NULL,
                response_code INTEGER NULL,
                reason_code INTEGER NOT NULL,
                bounce_type INTEGER NOT NULL,
                last_import_id INTEGER NOT NULL,
                PRIMARY KEY (tracking_key, recipient_address_id)
            ) WITHOUT ROWID;
            INSERT INTO imports (
                import_id, source_path, source_file_name, source_hash, imported_at,
                report_start, report_end, row_count, delivered_count, bounce_count,
                underway_count, archive_path)
            VALUES (1, 'legacy.zip', 'legacy.zip', 'legacy-hash', '2026-07-11T00:00:00Z',
                    '2026-07-10T00:00:00Z', '2026-07-10T23:59:59Z', $rowCount, $rowCount, 0, 0, 'legacy.zip');
            INSERT INTO mail_domains (domain_id, domain_name) VALUES (1, 'example.com'), (2, 'example.net');
            INSERT INTO mail_addresses (address_id, local_part, domain_id) VALUES (1, 'sender', 1), (2, 'recipient', 2);
            """;
        command.Parameters.AddWithValue("$rowCount", rowCount);
        command.ExecuteNonQuery();

        command.CommandText = """
            INSERT INTO mail_items (
                tracking_key, recipient_address_id, recipient_domain_id, sender_address_id,
                sender_domain_id, accepted_at, status, last_seen_at, duration_seconds,
                response_code, reason_code, bounce_type, last_import_id)
            VALUES ($trackingKey, 2, 2, 1, 1, $acceptedAt, 1, $acceptedAt, 60, 250, 0, 0, 1);
            """;
        command.Parameters.Clear();
        SqliteParameter trackingKey = command.Parameters.Add("$trackingKey", SqliteType.Blob);
        command.Parameters.AddWithValue("$acceptedAt", DateTime.Today.AddDays(-1).Ticks);
        for (int index = 0; index < rowCount; index++)
        {
            trackingKey.Value = BitConverter.GetBytes(index + 1);
            command.ExecuteNonQuery();
        }
    }}