using MailLogInspector.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class MailLogInspectorPortalReadOnlyTests
{
    [Fact]
    public void HasImportedSourceHashReadOnly_FindsExistingHash()
    {
        string databasePath = CreateDatabase();
        InsertImport(databasePath, "daily-hash", new DateTime(2026, 7, 17), new DateTime(2026, 7, 17, 23, 59, 0), 100);
        var store = new MailLogInspectorStore(databasePath);

        Assert.True(store.HasImportedSourceHashReadOnly("daily-hash"));
        Assert.False(store.HasImportedSourceHashReadOnly("missing-hash"));
    }

    [Fact]
    public void ReadLatestDailyImportReportDayReadOnly_IgnoresLongRangeImports()
    {
        string databasePath = CreateDatabase();
        InsertImport(databasePath, "daily-old", new DateTime(2026, 7, 16), new DateTime(2026, 7, 16, 23, 59, 0), 100);
        InsertImport(databasePath, "daily-latest", new DateTime(2026, 7, 17), new DateTime(2026, 7, 17, 23, 59, 0), 100);
        InsertImport(databasePath, "bulk", new DateTime(2026, 6, 1), new DateTime(2026, 7, 18), 1_000);
        var store = new MailLogInspectorStore(databasePath);

        Assert.Equal(new DateTime(2026, 7, 17), store.ReadLatestDailyImportReportDayReadOnly());
    }

    [Fact]
    public void PortalReadOnlyQueries_TreatDatabaseWithoutImportsTableAsEmpty()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "mail-log-inspector-portal-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string databasePath = Path.Combine(root, "mail-log-inspector.sqlite");
        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            connection.Open();
        }

        var store = new MailLogInspectorStore(databasePath);

        Assert.False(store.HasImportedSourceHashReadOnly("unknown-hash"));
        Assert.Null(store.ReadLatestDailyImportReportDayReadOnly());
    }

    private static string CreateDatabase()
    {
        string root = Path.Combine(Path.GetTempPath(), "mail-log-inspector-portal-read-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string databasePath = Path.Combine(root, "mail-log-inspector.sqlite");
        new MailLogInspectorStore(databasePath).Initialize();
        return databasePath;
    }

    private static void InsertImport(string databasePath, string hash, DateTime start, DateTime end, int rowCount)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO imports (
                source_path,
                source_file_name,
                source_hash,
                imported_at,
                report_start,
                report_end,
                row_count,
                delivered_count,
                bounce_count,
                underway_count,
                archive_path
            )
            VALUES (
                $sourcePath,
                $sourceFileName,
                $sourceHash,
                $importedAt,
                $reportStart,
                $reportEnd,
                $rowCount,
                0,
                0,
                0,
                NULL
            );
            """;
        command.Parameters.AddWithValue("$sourcePath", hash + ".zip");
        command.Parameters.AddWithValue("$sourceFileName", hash + ".zip");
        command.Parameters.AddWithValue("$sourceHash", hash);
        command.Parameters.AddWithValue("$importedAt", DateTime.UtcNow);
        command.Parameters.AddWithValue("$reportStart", start);
        command.Parameters.AddWithValue("$reportEnd", end);
        command.Parameters.AddWithValue("$rowCount", rowCount);
        command.ExecuteNonQuery();
    }
}
