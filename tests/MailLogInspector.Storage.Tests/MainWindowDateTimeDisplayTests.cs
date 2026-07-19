using System;
using MailLogInspector.App;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class MainWindowDateTimeDisplayTests
{
    [Fact]
    public void FormatDateTime_UsesDutchDayMonthYearOrder()
    {
        Assert.Equal("08-07-2026 21:05", MailLogInspectorDisplayFormats.DateTime(new DateTime(2026, 7, 8, 21, 5, 0)));
    }

    [Fact]
    public void FormatDateTimeOffset_UsesDutchDayMonthYearOrder()
    {
        Assert.Equal("08-07-2026 21:05", MailLogInspectorDisplayFormats.DateTime(new DateTimeOffset(2026, 7, 8, 21, 5, 0, TimeSpan.FromHours(2))));
    }

    [Fact]
    public void FormatNullableDateTime_UsesDashForMissingValue()
    {
        Assert.Equal("-", MailLogInspectorDisplayFormats.DateTime((DateTime?)null));
    }
    [Fact]
    public void UiDateTimeDisplays_DoNotUseCultureDependentGeneralFormat()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        foreach (string relativePath in new[]
        {
            Path.Combine("src", "MailLogInspector.App", "MainWindow.xaml"),
            Path.Combine("src", "MailLogInspector.App", "MainWindow.xaml.cs"),
            Path.Combine("src", "MailLogInspector.App", "SearchResultsExcelExporter.cs")
        })
        {
            string text = File.ReadAllText(Path.Combine(repoRoot, relativePath));
            Assert.DoesNotContain("ToString(\"g\")", text, StringComparison.Ordinal);
            Assert.DoesNotContain("StringFormat=g", text, StringComparison.Ordinal);
        }
    }
}
