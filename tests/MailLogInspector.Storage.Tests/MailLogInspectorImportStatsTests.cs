using MailLogInspector.Core;
using MailLogInspector.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class MailLogInspectorImportStatsTests
{
    [Fact]
    public void ReadRecentImports_ReturnsStatusCountsAndBounceCausesPerImport()
    {
        string root = Path.Combine(Path.GetTempPath(), "mail-log-import-stats-" + Guid.NewGuid().ToString("N"));
        var store = new MailLogInspectorStore(Path.Combine(root, "mail-log-inspector.sqlite"));
        store.Initialize();
        DateTime accepted = new(2026, 7, 10, 8, 0, 0);

        store.SaveImport(
            "stats.csv",
            "hash-stats",
            null,
            new[]
            {
                Entry(1, accepted, "D", "250", "delivered", "track-1", "a@example.com"),
                Entry(2, accepted, "D", "250", "delivered", "track-2", "b@example.com"),
                Entry(3, accepted, "B", "550", "invalid recipient", "track-3", "c@example.com"),
                Entry(4, accepted, "B", "552", "mailbox full", "track-4", "d@example.com"),
                Entry(5, accepted, "B", "554", "blocked by policy", "track-5", "e@example.com"),
                Entry(6, accepted, "Q", "", "queued", "track-6", "f@example.com"),
            },
            errorCount: 0);

        MailLogInspectorImportedFile import = Assert.Single(store.ReadRecentImports());

        Assert.Equal(6, import.RowCount);
        Assert.Equal(2, import.DeliveredCount);
        Assert.Equal(3, import.BounceCount);
        Assert.Equal(1, import.UnderwayCount);
        Assert.Equal("33,3%", import.DeliveredPercentDisplay);

        Assert.Collection(
            import.BounceCauses,
            cause =>
            {
                Assert.Equal("Adres ongeldig", cause.Label);
                Assert.Equal(1, cause.Count);
                Assert.Equal(100.0, cause.BarValue);
            },
            cause =>
            {
                Assert.Equal("Mailbox vol", cause.Label);
                Assert.Equal(1, cause.Count);
                Assert.Equal(100.0, cause.BarValue);
            },
            cause =>
            {
                Assert.Equal("Policy block", cause.Label);
                Assert.Equal(1, cause.Count);
                Assert.Equal(100.0, cause.BarValue);
            });
    }

    [Fact]
    public void RecentImportStatusCounts_ArePersistedOnImportsTable()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string schema = File.ReadAllText(Path.Combine(repoRoot, "src", "MailLogInspector.Storage", "MailLogInspectorSchema.cs"));
        string store = File.ReadAllText(Path.Combine(repoRoot, "src", "MailLogInspector.Storage", "MailLogInspectorStore.cs"));
        int queryStart = store.IndexOf("CommandText = \"SELECT import_id", StringComparison.Ordinal);
        int queryEnd = store.IndexOf("val2.Parameters.AddWithValue", queryStart, StringComparison.Ordinal);
        string readRecentImportsQuery = store.Substring(queryStart, queryEnd - queryStart);

        Assert.Contains("delivered_count INTEGER NOT NULL DEFAULT 0", schema, StringComparison.Ordinal);
        Assert.Contains("bounce_count INTEGER NOT NULL DEFAULT 0", schema, StringComparison.Ordinal);
        Assert.Contains("underway_count INTEGER NOT NULL DEFAULT 0", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("BackfillImportStatusCounts(connection)", schema, StringComparison.Ordinal);
        Assert.Contains("import_reason_counts", schema, StringComparison.Ordinal);
        Assert.Contains("DROP INDEX IF EXISTS ix_mail_log_inspector_items_import_status", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("GROUP BY last_import_id", readRecentImportsQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportCsvAsync_DoesNotArchiveDuplicateSourceTwice()
    {
        string root = Path.Combine(Path.GetTempPath(), "mail-log-duplicate-" + Guid.NewGuid().ToString("N"));
        MailLogInspectorWorkspacePaths workspace = MailLogInspectorWorkspaceBootstrapper.Prepare(root);
        var store = new MailLogInspectorStore(workspace.DatabasePath);
        store.Initialize();
        string csvPath = Path.Combine(root, "duplicate.csv");
        await File.WriteAllTextAsync(csvPath,
            "Date accepted,Date delivered,Mail from,Recipient,Status,Response code,Response message,Bounce class,Tries,Sender id,Tracking id,Campaign id\n" +
            "\"7/10/2026 8:00AM\",\"7/10/2026 8:01AM\",\"sender@example.com\",\"recipient@example.net\",\"D\",\"250\",\"ok\",\"\",\"1\",\"sender-1\",\"track-1\",\"campaign-1\"\n");
        var importer = new MailLogInspectorImportService(store, workspace);

        await importer.ImportCsvAsync(csvPath, CancellationToken.None);
        MailLogInspectorImportResult duplicate = await importer.ImportCsvAsync(csvPath, CancellationToken.None);

        Assert.True(duplicate.AlreadyImported);
        Assert.Single(Directory.EnumerateFiles(workspace.ArchiveDirectory, "*.csv"));
    }
    [Fact]
    public void DeferredImports_RebuildAnalysisOnlyWhenBatchIsFinalized()
    {
        string root = Path.Combine(Path.GetTempPath(), "mail-log-batch-" + Guid.NewGuid().ToString("N"));
        var store = new MailLogInspectorStore(Path.Combine(root, "mail-log-inspector.sqlite"));
        store.Initialize();
        DateTime accepted = new(2026, 7, 10, 8, 0, 0);

        store.SaveImport("one.csv", "hash-one", null, new[] { Entry(1, accepted, "D", "250", "ok", "track-one", "one@example.net") }, 0, rebuildAnalysis: false);
        store.SaveImport("two.csv", "hash-two", null, new[] { Entry(2, accepted, "B", "550", "invalid", "track-two", "two@example.net") }, 0, rebuildAnalysis: false);
        var analysis = new MailLogInspectorAnalysisService(store);
        MailLogInspectorAnalysisSummary before = analysis.BuildSummary(accepted.Date, accepted.Date.AddDays(1).AddTicks(-1));

        store.RebuildAnalysisData();
        MailLogInspectorAnalysisSummary after = analysis.BuildSummary(accepted.Date, accepted.Date.AddDays(1).AddTicks(-1));

        Assert.Equal(0, before.TotalCount);
        Assert.Equal(2, after.TotalCount);
        Assert.Equal(1, after.DeliveredCount);
        Assert.Equal(1, after.BounceCount);
    }

    [Fact]
    public void ApplyRetention_RollsBackDeleteWhenAnalysisRebuildFails()
    {
        string root = Path.Combine(Path.GetTempPath(), "mail-log-retention-" + Guid.NewGuid().ToString("N"));
        var store = new MailLogInspectorStore(Path.Combine(root, "mail-log-inspector.sqlite"));
        store.Initialize();
        DateTime accepted = new(2026, 1, 1, 8, 0, 0);
        store.SaveImport(
            "old.csv",
            "hash-old",
            null,
            new[] { Entry(1, accepted, "D", "250", "ok", "old-track", "old@example.net") },
            0);
        using (var connection = new SqliteConnection($"Data Source={store.DatabasePath}"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                CREATE TRIGGER fail_analysis_rebuild
                BEFORE DELETE ON analysis_daily_status
                BEGIN
                    SELECT RAISE(ABORT, 'forced rebuild failure');
                END;
                """;
            command.ExecuteNonQuery();
        }

        Assert.Throws<SqliteException>(() => store.ApplyRetention(new DateTime(2026, 4, 1)));
        Assert.Equal(1, store.CountMailItems());
    }

    [Fact]
    public void SaveImport_RollsBackWhenCancellationOccursDuringWriteLoop()
    {
        string root = Path.Combine(Path.GetTempPath(), "mail-log-cancel-" + Guid.NewGuid().ToString("N"));
        var store = new MailLogInspectorStore(Path.Combine(root, "mail-log-inspector.sqlite"));
        store.Initialize();
        using var cancellation = new CancellationTokenSource();

        IEnumerable<SmtpLogEntry> Entries()
        {
            DateTime accepted = new(2026, 7, 10, 8, 0, 0);
            for (int index = 0; index < 2_000; index++)
            {
                if (index == 600)
                {
                    cancellation.Cancel();
                }

                yield return Entry(
                    index + 1,
                    accepted,
                    "D",
                    "250",
                    "ok",
                    "track-" + index,
                    $"recipient-{index}@example.net");
            }
        }

        Assert.Throws<OperationCanceledException>(() =>
            store.SaveImport(
                "cancel.csv",
                "hash-cancel",
                null,
                Entries(),
                0,
                cancellationToken: cancellation.Token));
        Assert.Equal(0, store.CountMailItems());
        Assert.Empty(store.ReadRecentImports());
    }
    private static SmtpLogEntry Entry(int row, DateTime accepted, string status, string responseCode, string responseMessage, string trackingId, string recipient)
    {
        return new SmtpLogEntry(
            row,
            accepted,
            status == "D" ? accepted.AddMinutes(1) : null,
            "sender@example.com",
            "example.com",
            recipient,
            "example.com",
            status,
            responseCode,
            responseMessage,
            string.Empty,
            null,
            string.Empty,
            trackingId,
            string.Empty);
    }
}