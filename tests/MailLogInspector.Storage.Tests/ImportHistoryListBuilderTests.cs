using MailLogInspector.App;
using MailLogInspector.Core;
using MailLogInspector.Storage;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class ImportHistoryListBuilderTests
{
    [Fact]
    public void Build_MergesGmailSuccessAndKeepsManualImports()
    {
        DateTime importedAt = new(2026, 7, 18, 0, 15, 0, DateTimeKind.Utc);
        MailLogInspectorImportedFile gmailImport = CreateImport(1, "gmail.zip", "HASH-GMAIL", importedAt, 137_032, 131_118, 1_811, 4_103);
        MailLogInspectorImportedFile manualImport = CreateImport(2, "manual.zip", "HASH-MANUAL", importedAt.AddMinutes(-5), 50, 45, 3, 2);
        GmailReportHistoryRow gmailHistory = CreateHistory(
            "gmail-message",
            importedAt.AddMinutes(-1),
            "gmail.zip",
            "ok",
            archived: true,
            sourceHash: "HASH-GMAIL");

        IReadOnlyList<ImportHistoryListItem> rows = ImportHistoryListBuilder.Build(
            [gmailImport, manualImport],
            [gmailHistory]);

        Assert.Equal(2, rows.Count);
        ImportHistoryListItem gmailRow = rows.Single(row => row.FileName == "gmail.zip");
        Assert.Equal("Gmail", gmailRow.Source);
        Assert.Equal("Gereed", gmailRow.Status);
        Assert.Equal(137_032, gmailRow.MailCount);
        Assert.Equal("17-07-2026", gmailRow.ReportPeriod);
        ImportHistoryListItem manualRow = rows.Single(row => row.FileName == "manual.zip");
        Assert.Equal("Handmatig", manualRow.Source);
        Assert.Equal("Geïmporteerd", manualRow.Status);
    }

    [Fact]
    public void Build_UsesRecordedDirectSource()
    {
        DateTime importedAt = new(2026, 7, 18, 1, 5, 0, DateTimeKind.Utc);
        MailLogInspectorImportedFile import = CreateImport(
            1,
            "direct.zip",
            "HASH-DIRECT",
            importedAt,
            100,
            95,
            3,
            2);
        var source = new ReportImportSourceRow(
            "HASH-DIRECT",
            ReportImportSource.SmtpDirect,
            "direct.zip",
            new DateTime(2026, 7, 17),
            importedAt);

        ImportHistoryListItem row = Assert.Single(ImportHistoryListBuilder.Build(
            [import],
            [],
            [source]));

        Assert.Equal(ReportImportSource.SmtpDirect, row.Source);
        Assert.Equal("Geïmporteerd", row.Status);
    }

    [Fact]
    public void Build_ShowsFailuresAndDeletionProblemsWithoutDuplicateRows()
    {
        DateTime importedAt = new(2026, 7, 18, 0, 15, 0, DateTimeKind.Utc);
        MailLogInspectorImportedFile import = CreateImport(1, "report.zip", "HASH-REPORT", importedAt, 10, 8, 1, 1);
        GmailReportHistoryRow deletionFailure = CreateHistory(
            "message-ok",
            importedAt,
            "report.zip",
            "ok",
            archived: false,
            sourceHash: "HASH-REPORT",
            errorText: "Verwijderen mislukt.");
        GmailReportHistoryRow duplicate = CreateHistory(
            "message-duplicate",
            importedAt.AddMinutes(-1),
            "report.zip",
            "duplicate",
            archived: true,
            sourceHash: "HASH-REPORT");
        GmailReportHistoryRow downloadFailure = CreateHistory(
            "message-failed",
            importedAt.AddMinutes(1),
            "failed.zip",
            "failed",
            archived: false,
            sourceHash: null,
            downloadStatus: "failed",
            errorText: "Download mislukt.");

        IReadOnlyList<ImportHistoryListItem> rows = ImportHistoryListBuilder.Build(
            [import],
            [deletionFailure, duplicate, downloadFailure]);

        Assert.Equal(2, rows.Count);
        ImportHistoryListItem importRow = rows.Single(row => row.FileName == "report.zip");
        Assert.Equal("Import gereed, verwijderen mislukt", importRow.Status);
        Assert.Equal("Verwijderen mislukt.", importRow.ErrorText);
        ImportHistoryListItem failureRow = rows.Single(row => row.FileName == "failed.zip");
        Assert.Equal("Download mislukt", failureRow.Status);
        Assert.Null(failureRow.MailCount);
        Assert.Equal("-", failureRow.MailCountDisplay);
        Assert.Equal(failureRow, rows[0]);
    }

    private static MailLogInspectorImportedFile CreateImport(
        long importId,
        string fileName,
        string sourceHash,
        DateTime importedAt,
        int rows,
        int delivered,
        int bounce,
        int underway)
    {
        return new MailLogInspectorImportedFile(
            importId,
            Path.Combine("C:\\imports", fileName),
            fileName,
            sourceHash,
            importedAt,
            new DateTime(2026, 7, 17, 0, 0, 0),
            new DateTime(2026, 7, 17, 23, 59, 0),
            rows,
            null,
            delivered,
            bounce,
            underway);
    }

    private static GmailReportHistoryRow CreateHistory(
        string messageId,
        DateTime timestamp,
        string fileName,
        string importStatus,
        bool archived,
        string? sourceHash,
        string downloadStatus = "ok",
        string? errorText = null)
    {
        return new GmailReportHistoryRow(
            messageId,
            new DateTimeOffset(timestamp),
            "no-reply@smtp.com",
            "SMTP.com Periodic Report",
            "https://example.test/" + fileName,
            downloadStatus,
            importStatus,
            archived,
            errorText,
            timestamp,
            timestamp,
            sourceHash);
    }
}
