using System;
using MailLogInspector.Core;
using MailLogInspector.Storage;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class MailLogInspectorSearchStatusDisplayTests
{
    [Fact]
    public async Task Search_UsesBounceReasonAsVisibleStatusForKnownBounce()
    {
        await using var harness = await MailLogInspectorTestHarness.CreateAsync(
            new SmtpCsvRow("7/5/2026 10:00AM", "7/5/2026 10:01AM", "sender@example.com", "mailbox@example.net", "B", "track-1", "552", "Mailbox full", "Soft"),
            new SmtpCsvRow("7/5/2026 10:05AM", "7/5/2026 10:06AM", "sender@example.com", "invalid@example.net", "B", "track-2", "550", "User unknown", "Hard"));

        var service = new MailLogInspectorSearchService(harness.Store);

        var rows = service.Search(new MailLogInspectorSearchCriteria(
            new DateTime(2026, 7, 5, 0, 0, 0),
            new DateTime(2026, 7, 5, 23, 59, 59),
            null,
            null,
            null,
            null,
            null));

        Assert.Contains(rows, row => row.Status == "bounce" && row.Recipient == "mailbox@example.net" && row.StatusDisplay == "Mailbox vol");
        Assert.Contains(rows, row => row.Status == "bounce" && row.Recipient == "invalid@example.net" && row.StatusDisplay == "Adres ongeldig");
    }

    [Fact]
    public async Task ReadSummary_AppliesStatusBeforeCountingResults()
    {
        await using var harness = await MailLogInspectorTestHarness.CreateAsync(
            new SmtpCsvRow("7/5/2026 10:00AM", "7/5/2026 10:01AM", "sender@example.com", "delivered@example.net", "D", "track-delivered", "250", "Delivered", ""),
            new SmtpCsvRow("7/5/2026 10:05AM", "7/5/2026 10:06AM", "sender@example.com", "bounce@example.net", "B", "track-bounce", "550", "User unknown", "Hard"));
        var service = new MailLogInspectorSearchService(harness.Store);
        var criteria = new MailLogInspectorSearchCriteria(
            new DateTime(2026, 7, 5),
            new DateTime(2026, 7, 5, 23, 59, 59),
            null, null, null, null, "bounce");

        MailLogInspectorSearchSummary summary = service.ReadSummary(criteria);

        Assert.Equal(1, summary.TotalCount);
        Assert.Equal(0, summary.DeliveredCount);
        Assert.Equal(0, summary.UnderwayCount);
        Assert.Equal(1, summary.BounceCount);
    }
    [Fact]
    public void StatusDisplay_FallsBackToBounceWhenBounceReasonIsNotSpecific()
    {
        var row = new MailLogInspectorSearchRow(
            new DateTime(2026, 7, 5, 10, 0, 0),
            "sender@example.com",
            "recipient@example.net",
            string.Empty,
            "bounce",
            60,
            MailLogInspectorReasonCode.Other,
            "Overig",
            new DateTime(2026, 7, 5, 10, 0, 0),
            new DateTime(2026, 7, 5, 10, 1, 0),
            "import.csv");

        Assert.Equal("Bounce", row.StatusDisplay);
    }
}
