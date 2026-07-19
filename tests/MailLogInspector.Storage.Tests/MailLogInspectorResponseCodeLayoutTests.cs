using System.Collections.Generic;
using MailLogInspector.App;
using MailLogInspector.Core;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class MailLogInspectorResponseCodeLayoutTests
{
    [Fact]
    public void SplitResponseCodeRows_PutsExtraRowInLeftColumnAndPreservesOrder()
    {
        IReadOnlyList<MailLogInspectorValueMeaningCount> rows =
        [
            new("250", 10, "Accepted"),
            new("421", 8, "Temp unavailable"),
            new("450", 6, "Mailbox unavailable"),
            new("550", 4, "Rejected"),
            new("552", 2, "Quota exceeded")
        ];

        (IReadOnlyList<MailLogInspectorValueMeaningCount> left, IReadOnlyList<MailLogInspectorValueMeaningCount> right) = MainWindow.SplitResponseCodeRows(rows);

        Assert.Collection(left,
            row => Assert.Equal("250", row.Value),
            row => Assert.Equal("421", row.Value),
            row => Assert.Equal("450", row.Value));
        Assert.Collection(right,
            row => Assert.Equal("550", row.Value),
            row => Assert.Equal("552", row.Value));
    }

    [Fact]
    public void SplitResponseCodeRows_ReturnsTwoEmptyColumnsForEmptyInput()
    {
        (IReadOnlyList<MailLogInspectorValueMeaningCount> left, IReadOnlyList<MailLogInspectorValueMeaningCount> right) = MainWindow.SplitResponseCodeRows([]);

        Assert.Empty(left);
        Assert.Empty(right);
    }
}
