using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MailLogInspector.App;
using MailLogInspector.Core;
using MailLogInspector.Storage;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class MailLogInspectorMailHistoryTests
{
    /// <summary>
    /// De import bewaart een GUID-tracking-id als ruwe bytes. Zonder deze omkering kunnen we de
    /// mail niet terugvinden in het archief. De waarde komt uit een echt archiefbestand.
    /// </summary>
    [Fact]
    public void TrackingKeyBytesRoundTripToOriginalGuid()
    {
        var original = Guid.Parse("7baaad86-2426-42cf-84e7-08cadb774cf3");
        byte[] key = original.ToByteArray();

        Assert.Equal("86ADAA7B2624CF4284E708CADB774CF3", Convert.ToHexString(key));
        Assert.Equal(original, new Guid(key));
    }

    [Fact]
    public async Task ReadHistory_ReturnsEveryAttemptIncludingTheOnesNotStoredInTheDatabase()
    {
        string trackingId = "7baaad86-2426-42cf-84e7-08cadb774cf3";
        await using var harness = await MailLogInspectorTestHarness.CreateAsync(
            new SmtpCsvRow("7/20/2026 3:07PM", "7/20/2026 3:28PM", "sender@example.com", "target@example.net", "D", trackingId, "250", "ok"));

        // Het archief bevat naast de eindstand ook de tussenliggende, uitgestelde poging.
        WriteZipArchive(harness, "extra-report.zip",
            ("7/20/2026 3:07PM", "", "D-nee", "T", "451", "4.7.1 Greylisted"),
            ("7/20/2026 3:07PM", "7/20/2026 3:28PM", "ja", "D", "250", "ok"),
            trackingId);

        var service = new MailLogInspectorMailHistoryService(harness.Store);
        MailLogInspectorMailHistory history = service.ReadHistory(trackingId, "target@example.net");

        Assert.True(history.HasAttempts);
        Assert.Equal(2, history.Attempts.Count);
        Assert.Equal("Tijdelijk uitgesteld", history.Attempts[0].StatusDisplay);
        Assert.Equal("451", history.Attempts[0].ResponseCodeDisplay);
        Assert.Equal("Afgeleverd", history.Attempts[1].StatusDisplay);
        Assert.Equal("250", history.Attempts[1].ResponseCodeDisplay);
    }

    [Fact]
    public async Task ReadHistories_ReturnsCompleteHistoryForMultipleMailsInOneBatch()
    {
        string firstTrackingId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
        string secondTrackingId = "11111111-2222-3333-4444-555555555555";
        await using var harness = await MailLogInspectorTestHarness.CreateAsync(
            new SmtpCsvRow("7/20/2026 3:07PM", "7/20/2026 3:08PM", "sender@example.com", "first@example.net", "D", firstTrackingId));

        WriteZipArchive(harness, "second-report.zip",
            ("7/20/2026 3:10PM", "", "retry", "T", "451", "4.7.1 Greylisted"),
            ("7/20/2026 3:10PM", "7/20/2026 3:20PM", "delivered", "D", "250", "ok"),
            secondTrackingId,
            firstRecipient: "second@example.net",
            secondRecipient: "second@example.net");

        var service = new MailLogInspectorMailHistoryService(harness.Store);
        IReadOnlyList<MailLogInspectorMailHistory> histories = service.ReadHistories(
        [
            new MailLogInspectorMailHistoryRequest(firstTrackingId, "first@example.net"),
            new MailLogInspectorMailHistoryRequest(secondTrackingId, "second@example.net")
        ]);

        Assert.Equal(2, histories.Count);
        Assert.Single(histories[0].Attempts);
        Assert.Equal(firstTrackingId, histories[0].Attempts[0].TrackingId);
        Assert.Equal(2, histories[1].Attempts.Count);
        Assert.All(histories[1].Attempts, attempt => Assert.Equal(secondTrackingId, attempt.TrackingId));
    }

    [Fact]
    public async Task ReadHistories_SkipsArchivesOutsideTheKnownMailPeriod()
    {
        string trackingId = "12345678-1234-1234-1234-123456789012";
        DateTime acceptedAt = new(2026, 7, 20, 15, 7, 0);
        DateTime deliveredAt = new(2026, 7, 20, 15, 8, 0);
        await using var harness = await MailLogInspectorTestHarness.CreateAsync(
            new SmtpCsvRow("7/20/2026 3:07PM", "7/20/2026 3:08PM", "sender@example.com", "target@example.net", "D", trackingId));

        WriteZipArchive(harness, "old-report.zip",
            ("7/01/2026 3:07PM", "7/01/2026 3:08PM", "old", "D", "250", "ok"),
            ("7/01/2026 3:07PM", "7/01/2026 3:09PM", "old", "D", "250", "ok"),
            trackingId);
        SetImportPeriod(harness, "old-report.zip", new DateTime(2026, 7, 1), new DateTime(2026, 7, 1, 23, 59, 59));

        var service = new MailLogInspectorMailHistoryService(harness.Store);
        MailLogInspectorMailHistory history = Assert.Single(service.ReadHistories(
        [
            new MailLogInspectorMailHistoryRequest(trackingId, "target@example.net", acceptedAt, deliveredAt)
        ]));

        Assert.Single(history.Attempts);
        Assert.DoesNotContain("old-report.zip", history.SearchedArchives);
    }

    [Fact]
    public async Task ReadHistory_IgnoresOtherRecipientsThatShareTheTrackingId()
    {
        string trackingId = "11111111-2222-3333-4444-555555555555";
        await using var harness = await MailLogInspectorTestHarness.CreateAsync(
            new SmtpCsvRow("7/20/2026 3:07PM", "7/20/2026 3:08PM", "sender@example.com", "first@example.net", "D", trackingId));

        WriteZipArchive(harness, "shared.zip",
            ("7/20/2026 3:07PM", "7/20/2026 3:08PM", "first", "D", "250", "ok"),
            ("7/20/2026 3:07PM", "7/20/2026 3:09PM", "second", "D", "250", "ok"),
            trackingId,
            firstRecipient: "first@example.net",
            secondRecipient: "second@example.net");

        var service = new MailLogInspectorMailHistoryService(harness.Store);
        MailLogInspectorMailHistory history = service.ReadHistory(trackingId, "first@example.net");

        Assert.NotEmpty(history.Attempts);
        Assert.All(history.Attempts, attempt => Assert.Equal("first@example.net", attempt.Recipient));
        Assert.DoesNotContain(history.Attempts, attempt => attempt.Recipient == "second@example.net");
    }

    /// <summary>
    /// Een import kan een los CSV-bestand zijn. Ook dat archief moet doorzocht worden.
    /// </summary>
    [Fact]
    public async Task ReadHistory_ReadsPlainCsvArchivesAsWell()
    {
        string trackingId = "99999999-8888-7777-6666-555555555555";
        await using var harness = await MailLogInspectorTestHarness.CreateAsync(
            new SmtpCsvRow("7/20/2026 3:07PM", "7/20/2026 3:08PM", "sender@example.com", "target@example.net", "D", trackingId));

        var service = new MailLogInspectorMailHistoryService(harness.Store);
        MailLogInspectorMailHistory history = service.ReadHistory(trackingId, "target@example.net");

        Assert.Single(history.Attempts);
        Assert.NotEmpty(history.SearchedArchives);
    }

    /// <summary>
    /// Niet elk tracking-id is een GUID. Zo'n id staat niet letterlijk als sleutel in de database,
    /// maar de archiefopzoeking moet de mail dan alsnog terugvinden.
    /// </summary>
    [Fact]
    public async Task ReadHistory_FindsMailWhenTheTrackingIdIsNotAGuid()
    {
        await using var harness = await MailLogInspectorTestHarness.CreateAsync(
            new SmtpCsvRow("7/20/2026 3:07PM", "7/20/2026 3:28PM", "sender@example.com", "target@example.net", "D", "not-a-guid-123"));

        var searchService = new MailLogInspectorSearchService(harness.Store);
        var rows = searchService.Search(new MailLogInspectorSearchCriteria(
            new DateTime(2026, 7, 20, 0, 0, 0),
            new DateTime(2026, 7, 20, 23, 59, 59),
            null, null, null, null, null));

        MailLogInspectorSearchRow row = Assert.Single(rows);
        Assert.NotEqual(string.Empty, row.TrackingId);

        var service = new MailLogInspectorMailHistoryService(harness.Store);
        MailLogInspectorMailHistory history = service.ReadHistory(row.TrackingId, row.Recipient);

        Assert.True(history.HasAttempts);
        Assert.All(history.Attempts, attempt => Assert.Equal("not-a-guid-123", attempt.TrackingId));
    }

    [Fact]
    public async Task ReadHistory_ReportsNoAttemptsWhenTheMailIsNotInAnyArchive()
    {
        await using var harness = await MailLogInspectorTestHarness.CreateAsync(
            new SmtpCsvRow("7/20/2026 3:07PM", "7/20/2026 3:08PM", "sender@example.com", "target@example.net", "D", "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));

        var service = new MailLogInspectorMailHistoryService(harness.Store);
        MailLogInspectorMailHistory history = service.ReadHistory("00000000-0000-0000-0000-000000000000", "target@example.net");

        Assert.False(history.HasAttempts);
        Assert.Contains("Geen logregels gevonden", MailHistoryWindow.BuildStatusText(history), StringComparison.Ordinal);
    }

    [Fact]
    public void ReadHistory_WithoutTrackingIdReturnsEmptyResultInsteadOfScanningArchives()
    {
        var service = new MailLogInspectorMailHistoryService(new MailLogInspectorStore("does-not-exist.sqlite"));

        MailLogInspectorMailHistory history = service.ReadHistory(string.Empty, "target@example.net");

        Assert.False(history.HasAttempts);
        Assert.Empty(history.SearchedArchives);
    }

    [Fact]
    public void MissingArchivesAreMentionedInTheFooter()
    {
        var history = new MailLogInspectorMailHistory(
            "track",
            "target@example.net",
            Array.Empty<MailLogInspectorMailHistoryAttempt>(),
            new[] { "a.zip", "b.zip" },
            new[] { "c.zip" });

        string footer = MailHistoryWindow.BuildFooterText(history);

        Assert.Contains("2 archiefbestanden doorzocht", footer, StringComparison.Ordinal);
        Assert.Contains("1 archiefbestand ontbreekt", footer, StringComparison.Ordinal);
    }

    [Fact]
    public void AttemptWithoutDeliveryFallsBackToTheAcceptedMoment()
    {
        var attempt = new MailLogInspectorMailHistoryAttempt(
            new DateTime(2026, 7, 20, 15, 7, 0),
            null,
            "sender@example.com",
            "target@example.net",
            "T",
            "451",
            "greylisted",
            string.Empty,
            0,
            "track",
            "report.zip");

        Assert.Equal("20-07-2026 15:07", attempt.MomentDisplay);
        Assert.Equal("Tijdelijk uitgesteld", attempt.StatusDisplay);
        Assert.Equal("0", attempt.TriesDisplay);
    }

    private static void WriteZipArchive(
        MailLogInspectorTestHarness harness,
        string fileName,
        (string Accepted, string Delivered, string Marker, string Status, string Code, string Message) first,
        (string Accepted, string Delivered, string Marker, string Status, string Code, string Message) second,
        string trackingId,
        string firstRecipient = "target@example.net",
        string secondRecipient = "target@example.net")
    {
        var builder = new StringBuilder();
        builder.AppendLine("Date accepted,Date delivered,Mail from,Recipient,Status,Response code,Response message,Bounce class,Tries,Sender id,Tracking id,Campaign id");
        AppendRow(builder, first, trackingId, firstRecipient, tries: "0");
        AppendRow(builder, second, trackingId, secondRecipient, tries: "1");

        string zipPath = Path.Combine(harness.Workspace.ArchiveDirectory, fileName);
        using (var stream = File.Create(zipPath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = archive.CreateEntry("report.csv");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(builder.ToString());
        }

        RegisterImport(harness, fileName, zipPath);
    }

    private static void AppendRow(
        StringBuilder builder,
        (string Accepted, string Delivered, string Marker, string Status, string Code, string Message) row,
        string trackingId,
        string recipient,
        string tries)
    {
        builder.AppendLine(string.Join(",",
            Quote(row.Accepted),
            Quote(row.Delivered),
            Quote("sender@example.com"),
            Quote(recipient),
            Quote(row.Status),
            Quote(row.Code),
            Quote(row.Message),
            Quote(string.Empty),
            Quote(tries),
            Quote("sender-1"),
            Quote(trackingId),
            Quote("campaign-1")));
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    private static void RegisterImport(MailLogInspectorTestHarness harness, string fileName, string archivePath)
    {
        using var connection = harness.Store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO imports (source_path, source_file_name, source_hash, imported_at, archive_path, row_count)
            VALUES ($path, $name, $hash, $imported, $archive, 0);
            """;
        command.Parameters.AddWithValue("$path", archivePath);
        command.Parameters.AddWithValue("$name", fileName);
        command.Parameters.AddWithValue("$hash", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$imported", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$archive", archivePath);
        command.ExecuteNonQuery();
    }

    private static void SetImportPeriod(MailLogInspectorTestHarness harness, string fileName, DateTime reportStart, DateTime reportEnd)
    {
        using var connection = harness.Store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE imports
            SET report_start = $reportStart,
                report_end = $reportEnd
            WHERE source_file_name = $fileName;
            """;
        command.Parameters.AddWithValue("$reportStart", reportStart);
        command.Parameters.AddWithValue("$reportEnd", reportEnd);
        command.Parameters.AddWithValue("$fileName", fileName);
        command.ExecuteNonQuery();
    }
}
