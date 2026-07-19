using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Data.Sqlite;
using MailLogInspector.Core;
using MailLogInspector.Storage;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class MailLogInspectorAnalysisServiceTests
{
    [Fact]
    public async Task BuildSummary_FiltersExactSenderAddressByLocalPartAndDomain()
    {
        await using var harness = await MailLogInspectorTestHarness.CreateAsync(
            new SmtpCsvRow("7/5/2026 8:00AM", "7/5/2026 8:05AM", "shared@example.com", "target@dest.com", "D", "track-1"),
            new SmtpCsvRow("7/5/2026 8:10AM", "7/5/2026 8:15AM", "shared@other.com", "target@dest.com", "D", "track-2"));

        var service = new MailLogInspectorAnalysisService(harness.Store);

        var summary = service.BuildSummary(new MailLogInspectorSearchCriteria(
            new DateTime(2026, 7, 5, 0, 0, 0),
            new DateTime(2026, 7, 5, 23, 59, 59),
            "shared@example.com",
            null,
            null,
            null,
            null));

        Assert.Equal(1, summary.DeliveredCount);
        Assert.Collection(summary.TopSenderDomains, item => Assert.Equal("example.com", item.Value));
    }

    [Fact]
    public async Task BuildSummary_FiltersExactRecipientAddressByLocalPartAndDomain()
    {
        await using var harness = await MailLogInspectorTestHarness.CreateAsync(
            new SmtpCsvRow("7/5/2026 9:00AM", "7/5/2026 9:03AM", "sender@example.com", "shared@dest.com", "D", "track-1"),
            new SmtpCsvRow("7/5/2026 9:05AM", "7/5/2026 9:07AM", "sender@example.com", "shared@otherdest.com", "D", "track-2"));

        var service = new MailLogInspectorAnalysisService(harness.Store);

        var summary = service.BuildSummary(new MailLogInspectorSearchCriteria(
            new DateTime(2026, 7, 5, 0, 0, 0),
            new DateTime(2026, 7, 5, 23, 59, 59),
            null,
            "shared@dest.com",
            null,
            null,
            null));

        Assert.Equal(1, summary.DeliveredCount);
        Assert.Collection(summary.TopRecipientDomains, item => Assert.Equal("dest.com", item.Value));
    }

    [Fact]
    public async Task BuildSummary_ReturnsBreakdownsAndTechnicalCounts()
    {
        await using var harness = await MailLogInspectorTestHarness.CreateAsync(
            new SmtpCsvRow("7/5/2026 8:00AM", "7/5/2026 8:02AM", "alpha@sender-a.nl", "one@gmail.com", "D", "track-1", "250", "ok", ""),
            new SmtpCsvRow("7/5/2026 8:05AM", "7/5/2026 8:06AM", "beta@sender-a.nl", "two@gmail.com", "B", "track-2", "550", "User unknown", "Hard"),
            new SmtpCsvRow("7/5/2026 8:10AM", "7/5/2026 8:11AM", "gamma@sender-a.nl", "three@yahoo.com", "D", "track-3", "250", "ok", ""),
            new SmtpCsvRow("7/5/2026 8:15AM", "7/5/2026 8:18AM", "delta@sender-b.nl", "four@yahoo.com", "T", "track-4", "421", "Try again later", ""),
            new SmtpCsvRow("7/5/2026 8:20AM", "7/5/2026 8:21AM", "epsilon@sender-b.nl", "five@yahoo.com", "T", "track-5", "421", "Try again later", ""));

        var service = new MailLogInspectorAnalysisService(harness.Store);

        var summary = service.BuildSummary(new MailLogInspectorSearchCriteria(
            new DateTime(2026, 7, 5, 0, 0, 0),
            new DateTime(2026, 7, 5, 23, 59, 59),
            null,
            null,
            null,
            null,
            null));

        Assert.Equal(5, summary.TotalCount);
        Assert.Equal(2, summary.DeliveredCount);
        Assert.Equal(2, summary.UnderwayCount);
        Assert.Equal(1, summary.BounceCount);

        Assert.Collection(summary.SenderVolumeRows,
            first =>
            {
                Assert.Equal("sender-a.nl", first.Key);
                Assert.Equal(3, first.Total);
                Assert.Equal(2, first.Delivered);
                Assert.Equal(0, first.Underway);
                Assert.Equal(1, first.Bounce);
                Assert.Equal(1, first.ProblemCount);
            },
            second =>
            {
                Assert.Equal("sender-b.nl", second.Key);
                Assert.Equal(2, second.Total);
                Assert.Equal(0, second.Delivered);
                Assert.Equal(2, second.Underway);
                Assert.Equal(0, second.Bounce);
                Assert.Equal(2, second.ProblemCount);
            });

        Assert.Equal("sender-b.nl", summary.SenderLowestSuccessRows[0].Key);
        Assert.Equal("yahoo.com", summary.RecipientProblemVolumeRows[0].Key);
        Assert.Equal("yahoo.com", summary.RecipientHighestProblemRateRows[0].Key);

        Assert.Contains(summary.TopBounceCauses, row => row.Value == "550 Ongeldig" && row.Count == 1);
        Assert.Equal(1, summary.TopBounceCauses.Sum(static row => row.Count));
        Assert.Contains(summary.TopResponseCodes, row => row.Value == "421" && row.Count == 2 && row.Meaning == "Server tijdelijk niet beschikbaar");
        Assert.Contains(summary.TopResponseCodes, row => row.Value == "550" && row.Count == 1 && row.Meaning == "Ontvangeradres ongeldig of geweigerd");
    }

    [Fact]
    public async Task BuildSummary_NormalizesMissingResponseCodeToZero()
    {
        await using var harness = await MailLogInspectorTestHarness.CreateAsync(
            new SmtpCsvRow("7/5/2026 8:00AM", "7/5/2026 8:02AM", "alpha@sender-a.nl", "one@gmail.com", "D", "track-1", "", "ok", ""));

        var service = new MailLogInspectorAnalysisService(harness.Store);

        var summary = service.BuildSummary(new MailLogInspectorSearchCriteria(
            new DateTime(2026, 7, 5, 0, 0, 0),
            new DateTime(2026, 7, 5, 23, 59, 59),
            null,
            null,
            null,
            null,
            null));

        Assert.Contains(summary.TopResponseCodes, row => row.Value == "0" && row.Count == 1 && row.Meaning == "Geen bruikbare SMTP-code gevonden");
    }

    [Fact]
    public async Task BuildSummary_UsesDistinctDescriptionsForMailboxStorageResponseCodes()
    {
        await using var harness = await MailLogInspectorTestHarness.CreateAsync(
            new SmtpCsvRow("7/5/2026 8:00AM", "7/5/2026 8:02AM", "alpha@sender-a.nl", "one@gmail.com", "B", "track-1", "452", "insufficient storage", "Soft"),
            new SmtpCsvRow("7/5/2026 8:05AM", "7/5/2026 8:06AM", "beta@sender-a.nl", "two@gmail.com", "B", "track-2", "552", "mailbox full", "Soft"));

        var service = new MailLogInspectorAnalysisService(harness.Store);

        var summary = service.BuildSummary(new MailLogInspectorSearchCriteria(
            new DateTime(2026, 7, 5, 0, 0, 0),
            new DateTime(2026, 7, 5, 23, 59, 59),
            null,
            null,
            null,
            null,
            null));

        Assert.Contains(summary.TopResponseCodes, row => row.Value == "452" && row.Count == 1 && row.Meaning == "Onvoldoende opslag op ontvangende server");
        Assert.Contains(summary.TopResponseCodes, row => row.Value == "552" && row.Count == 1 && row.Meaning == "Mailbox vol of limiet overschreden");
    }

    [Fact]
    public async Task Import_StoresCompactReasonWithoutRawResponseMessage()
    {
        await using var harness = await MailLogInspectorTestHarness.CreateAsync(
            new SmtpCsvRow("7/5/2026 10:00AM", "7/5/2026 10:01AM", "sender@example.com", "recipient@example.net", "B", "track-9", "552", "Mailbox full", "Soft"));

        await using var connection = new SqliteConnection($"Data Source={harness.Store.DatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT response_code, reason_code FROM mail_items LIMIT 1;";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(552, reader.GetInt32(0));
        Assert.Equal((int)MailLogInspectorReasonCode.MailboxFull, reader.GetInt32(1));

        await using var rawMessageCommand = connection.CreateCommand();
        rawMessageCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'mail_item_messages';";
        Assert.Equal(0L, (long)(await rawMessageCommand.ExecuteScalarAsync() ?? 0L));
    }

    [Fact]
    public async Task Import_AllowsSameTrackingIdForMultipleRecipients()
    {
        await using var harness = await MailLogInspectorTestHarness.CreateAsync(
            new SmtpCsvRow("7/5/2026 10:00AM", "7/5/2026 10:01AM", "sender@example.com", "one@example.net", "D", "shared-track"),
            new SmtpCsvRow("7/5/2026 10:00AM", "7/5/2026 10:01AM", "sender@example.com", "two@example.net", "D", "shared-track"));

        var service = new MailLogInspectorSearchService(harness.Store);
        var rows = service.Search(new MailLogInspectorSearchCriteria(
            new DateTime(2026, 7, 5, 0, 0, 0),
            new DateTime(2026, 7, 5, 23, 59, 59),
            null,
            null,
            null,
            null,
            null));

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.Recipient == "one@example.net");
        Assert.Contains(rows, row => row.Recipient == "two@example.net");
        Assert.All(rows, row => Assert.Equal(string.Empty, row.TrackingId));
    }

    [Fact]
    public async Task BuildSearchCommand_UsesRecipientAddressIdForExactRecipientEmail()
    {
        await using var harness = await MailLogInspectorTestHarness.CreateAsync(
            new SmtpCsvRow("7/5/2026 10:00AM", "7/5/2026 10:01AM", "sender@example.com", "one@example.net", "D", "track-1"),
            new SmtpCsvRow("7/5/2026 10:02AM", "7/5/2026 10:03AM", "sender@example.com", "two@example.net", "D", "track-2"));

        await using var connection = new SqliteConnection($"Data Source={harness.Store.DatabasePath}");
        await connection.OpenAsync();

        var command = BuildSearchCommand(connection, new MailLogInspectorSearchCriteria(
            new DateTime(2026, 7, 5, 0, 0, 0),
            new DateTime(2026, 7, 5, 23, 59, 59),
            null,
            "one@example.net",
            null,
            null,
            null));

        Assert.Contains("item.recipient_address_id = $recipientAddressId", command.CommandText);
        Assert.DoesNotContain("recipient.local_part =", command.CommandText);
        Assert.DoesNotContain("IS NULL OR", command.CommandText);
        Assert.Contains(command.Parameters.Cast<SqliteParameter>(), parameter => parameter.ParameterName == "$recipientAddressId");
    }

    [Fact]
    public async Task BuildSearchCommand_UsesDirectPredicatesForDateAndDomainFilters()
    {
        await using var harness = await MailLogInspectorTestHarness.CreateAsync(
            new SmtpCsvRow("7/5/2026 10:00AM", "7/5/2026 10:01AM", "alpha@sender-a.nl", "one@example.net", "D", "track-1"));

        await using var connection = new SqliteConnection($"Data Source={harness.Store.DatabasePath}");
        await connection.OpenAsync();

        var command = BuildSearchCommand(connection, new MailLogInspectorSearchCriteria(
            new DateTime(2026, 7, 5, 0, 0, 0),
            new DateTime(2026, 7, 5, 23, 59, 59),
            null,
            null,
            "sender-a.nl",
            "example.net",
            null));

        Assert.Contains("item.accepted_at >= $fromInclusive", command.CommandText);
        Assert.Contains("item.accepted_at <= $throughInclusive", command.CommandText);
        Assert.Contains("item.sender_domain_id = $senderDomainId", command.CommandText);
        Assert.Contains("item.recipient_domain_id = $recipientDomainId", command.CommandText);
        Assert.DoesNotContain("IS NULL OR", command.CommandText);
    }

    [Fact]
    public async Task BuildSummaryCommand_UsesDirectPredicatesForExactRecipientAndSenderDomain()
    {
        await using var harness = await MailLogInspectorTestHarness.CreateAsync(
            new SmtpCsvRow("7/5/2026 10:00AM", "7/5/2026 10:01AM", "alpha@sender-a.nl", "one@example.net", "D", "track-1"));

        await using var connection = new SqliteConnection($"Data Source={harness.Store.DatabasePath}");
        await connection.OpenAsync();

        var command = BuildSummaryCommand(connection, new MailLogInspectorSearchCriteria(
            new DateTime(2026, 7, 5, 0, 0, 0),
            new DateTime(2026, 7, 5, 23, 59, 59),
            null,
            "one@example.net",
            "sender-a.nl",
            null,
            null));

        Assert.Contains("item.sender_domain_id = $senderDomainId", command.CommandText);
        Assert.Contains("item.recipient_address_id = $recipientAddressId", command.CommandText);
        Assert.DoesNotContain("IS NULL OR", command.CommandText);
    }

    [Fact]
    public async Task ImportCsv_SplitsRowsBetweenActiveDatabaseAndMonthlyArchives()
    {
        DateTime recent = DateTime.Today.AddDays(-1);
        DateTime oldA = DateTime.Today.AddDays(-(MailLogInspectorRetentionPolicy.ActiveRetentionDays + 5));
        DateTime oldB = DateTime.Today.AddMonths(-4);

        await using var harness = await MailLogInspectorTestHarness.CreateAsync(
            new SmtpCsvRow(FormatCsvDate(recent), FormatCsvDate(recent.AddMinutes(1)), "recent@example.com", "target@example.net", "D", "track-recent"),
            new SmtpCsvRow(FormatCsvDate(oldA), FormatCsvDate(oldA.AddMinutes(1)), "old-a@example.com", "target@example.net", "D", "track-old-a"),
            new SmtpCsvRow(FormatCsvDate(oldB), FormatCsvDate(oldB.AddMinutes(1)), "old-b@example.com", "target@example.net", "D", "track-old-b"));

        Assert.Equal(1, harness.Store.CountMailItems());

        string oldAArchivePath = Path.Combine(harness.Workspace.ArchiveDatabaseDirectory, oldA.ToString("yyyy-MM", CultureInfo.InvariantCulture) + ".sqlite");
        string oldBArchivePath = Path.Combine(harness.Workspace.ArchiveDatabaseDirectory, oldB.ToString("yyyy-MM", CultureInfo.InvariantCulture) + ".sqlite");

        Assert.True(File.Exists(oldAArchivePath));
        Assert.True(File.Exists(oldBArchivePath));

        var oldAStore = new MailLogInspectorStore(oldAArchivePath);
        var oldBStore = new MailLogInspectorStore(oldBArchivePath);

        Assert.Equal(1, oldAStore.CountMailItems());
        Assert.Equal(1, oldBStore.CountMailItems());
    }

    private static string FormatCsvDate(DateTime value)
    {
        return value.ToString("M/d/yyyy h:mmtt", CultureInfo.InvariantCulture);
    }

    private static SqliteCommand BuildSearchCommand(SqliteConnection connection, MailLogInspectorSearchCriteria criteria, int limit = 500)
    {
        MethodInfo? method = typeof(MailLogInspectorSearchService).GetMethod("BuildSearchCommand", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        object? command = method.Invoke(null, new object?[] { connection, criteria, limit });
        return Assert.IsType<SqliteCommand>(command);
    }

    private static SqliteCommand BuildSummaryCommand(SqliteConnection connection, MailLogInspectorSearchCriteria criteria)
    {
        MethodInfo? method = typeof(MailLogInspectorSearchService).GetMethod("BuildSummaryCommand", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        object? command = method.Invoke(null, new object?[] { connection, criteria });
        return Assert.IsType<SqliteCommand>(command);
    }
}

internal sealed class MailLogInspectorTestHarness : IAsyncDisposable
{
    private readonly string _rootPath;

    private MailLogInspectorTestHarness(string rootPath, MailLogInspectorWorkspacePaths workspace, MailLogInspectorStore store)
    {
        _rootPath = rootPath;
        Workspace = workspace;
        Store = store;
    }

    public MailLogInspectorWorkspacePaths Workspace { get; }

    public MailLogInspectorStore Store { get; }

    public static async Task<MailLogInspectorTestHarness> CreateAsync(params SmtpCsvRow[] rows)
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "MailLogInspectorTests", Guid.NewGuid().ToString("N"));
        var workspace = MailLogInspectorWorkspaceBootstrapper.Prepare(rootPath);
        var store = new MailLogInspectorStore(workspace.DatabasePath);
        store.Initialize();

        var csvPath = Path.Combine(rootPath, "import.csv");
        await File.WriteAllTextAsync(csvPath, BuildCsv(rows), Encoding.UTF8);

        var importer = new MailLogInspectorImportService(store, workspace);
        await importer.ImportCsvAsync(csvPath, CancellationToken.None);

        return new MailLogInspectorTestHarness(rootPath, workspace, store);
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(_rootPath, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }

        return ValueTask.CompletedTask;
    }

    private static string BuildCsv(IEnumerable<SmtpCsvRow> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Date accepted,Date delivered,Mail from,Recipient,Status,Response code,Response message,Bounce class,Tries,Sender id,Tracking id,Campaign id");
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(",",
                Csv(row.AcceptedAt),
                Csv(row.DeliveredAt),
                Csv(row.MailFrom),
                Csv(row.Recipient),
                Csv(row.Status),
                Csv(row.ResponseCode),
                Csv(row.ResponseMessage),
                Csv(row.BounceClass),
                Csv("1"),
                Csv("sender-1"),
                Csv(row.TrackingId),
                Csv("campaign-1")));
        }

        return builder.ToString();
    }

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}

internal sealed record SmtpCsvRow(
    string AcceptedAt,
    string DeliveredAt,
    string MailFrom,
    string Recipient,
    string Status,
    string TrackingId,
    string ResponseCode = "250",
    string ResponseMessage = "ok",
    string BounceClass = "");
