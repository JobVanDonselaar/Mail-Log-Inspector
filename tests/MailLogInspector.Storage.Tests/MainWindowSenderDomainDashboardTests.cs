using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using MailLogInspector.Core;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class MainWindowSenderDomainDashboardTests
{
    [Fact]
    public void MainWindowStaticResourcesResolveToDeclaredKeys()
    {
        string root = RepositoryRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "MailLogInspector.App", "MainWindow.xaml"));
        string[] keys = Regex.Matches(xaml, "x:Key=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        string[] references = Regex.Matches(xaml, "\\{StaticResource\\s+([^}\\s]+)\\}")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.All(references, reference => Assert.Contains(reference, keys));
    }
    [Fact]
    public void SearchLayoutOffersOptionalSenderDomainDashboardAndFiveThousandLimit()
    {
        string root = RepositoryRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "MailLogInspector.App", "MainWindow.xaml"));
        string code = File.ReadAllText(Path.Combine(root, "src", "MailLogInspector.App", "MainWindow.SearchSenderDashboard.cs"));

        Assert.Contains("Name=\"SenderDomainDashboardCheckBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Domeinanalyse tonen\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"SearchUnderwayMetricCard\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"SearchResultsPanel\" Grid.Column=\"0\" Grid.ColumnSpan=\"5\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"SenderDomainDashboardPanel\" Grid.Column=\"3\" Grid.ColumnSpan=\"2\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumnSpan(SearchResultsPanel, 3)", code, StringComparison.Ordinal);
        Assert.Contains("SearchResultsPanel.Margin = new Thickness(0, 0, 12, 0)", code, StringComparison.Ordinal);
        int layoutStart = xaml.IndexOf("<Grid Name=\"SearchResultsDashboardLayoutGrid\"", StringComparison.Ordinal);
        int layoutColumnsEnd = xaml.IndexOf("</Grid.ColumnDefinitions>", layoutStart, StringComparison.Ordinal);
        string layoutColumns = xaml.Substring(layoutStart, layoutColumnsEnd - layoutStart);
        Assert.Equal(5, CountOccurrences(layoutColumns, "<ColumnDefinition Width=\"*\" />"));
        Assert.Contains("<ComboBoxItem Content=\"5000\" Tag=\"5000\" />", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DomainDashboardShowsDurationDistributionAndReadableVolumeScale()
    {
        string root = RepositoryRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "MailLogInspector.App", "MainWindow.xaml"));
        string code = File.ReadAllText(Path.Combine(root, "src", "MailLogInspector.App", "MainWindow.SearchSenderDashboard.cs"));

        Assert.DoesNotContain("Totalen geselecteerde periode", xaml, StringComparison.Ordinal);
        Assert.Contains("Afleververtraging geselecteerde periode", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"SenderDomainTrendMiddleTextBlock\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"SenderDurationWithinOneSummaryTextBlock\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"SenderDurationDelayedSummaryTextBlock\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"SenderDurationDelayItemsControl\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Name=\"SenderDurationWithinOneColumn\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Langer dan 1 min", xaml, StringComparison.Ordinal);
        Assert.Contains("SenderDurationDelayBar", code, StringComparison.Ordinal);
        Assert.Contains("delayedMaximum", code, StringComparison.Ordinal);
        Assert.Contains("FormatDurationDelaySummary", code, StringComparison.Ordinal);
        Assert.Contains("RoundTrendMaximum", code, StringComparison.Ordinal);
        Assert.Contains("DurationDistribution", code, StringComparison.Ordinal);
    }
    [Theory]
    [InlineData(518, 518)]
    [InlineData(350, 350)]
    [InlineData(1000, 1000)]
    public void SenderTrendScaleUsesTightReadableMaximum(int value, int expected)
    {
        MethodInfo method = typeof(MailLogInspector.App.MainWindow).GetMethod(
            "RoundTrendMaximum",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        Assert.Equal(expected, method.Invoke(null, [value]));
    }

    [Fact]
    public void SenderTrendBarsExposeCompleteDayTooltipAcrossTheColumn()
    {
        string root = RepositoryRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "MailLogInspector.App", "MainWindow.xaml"));
        var day = new MailLogInspector.Core.MailLogInspectorSenderDomainTrendDay(
            new DateTime(2026, 7, 17),
            140000,
            131114,
            1075,
            1811,
            131114,
            0,
            35,
            MailLogInspector.Core.MailLogInspectorDurationBucket.WithinOneMinute);

        MethodInfo method = typeof(MailLogInspector.App.MainWindow).GetMethod(
            "BuildSenderDomainTrendTooltip",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        string tooltip = Assert.IsType<string>(method.Invoke(null, [day]));

        Assert.Contains("17-07-2026", tooltip, StringComparison.Ordinal);
        Assert.Contains("131.114 afgeleverd", tooltip, StringComparison.Ordinal);
        Assert.Contains("gemiddeld 35s", tooltip, StringComparison.Ordinal);
        Assert.Contains("95% binnen 1 min", tooltip, StringComparison.Ordinal);
        Assert.Contains("Background=\"Transparent\" ToolTip=\"{Binding ToolTip}\"", xaml, StringComparison.Ordinal);
    }
    [Fact]
    public void SenderDomainDashboardDefaultsOnWithoutForgettingPreference()
    {
        string root = RepositoryRoot();
        string mainCode = File.ReadAllText(Path.Combine(root, "src", "MailLogInspector.App", "MainWindow.xaml.cs"));
        string dashboardCode = File.ReadAllText(Path.Combine(root, "src", "MailLogInspector.App", "MainWindow.SearchSenderDashboard.cs"));

        Assert.Contains("SenderDomainDashboardCheckBox.IsChecked = true", mainCode, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(mainCode, "SenderDomainDashboardCheckBox.IsChecked = true"));
        Assert.DoesNotContain("SenderDomainDashboardCheckBox.IsChecked = false", dashboardCode, StringComparison.Ordinal);
    }
    [Fact]
    public void DashboardEligibilityUsesDomainOnlyAndBlankRecipient()
    {
        string root = RepositoryRoot();
        string code = File.ReadAllText(Path.Combine(root, "src", "MailLogInspector.App", "MainWindow.SearchSenderDashboard.cs"));

        Assert.Contains("SenderDomain is not null", code, StringComparison.Ordinal);
        Assert.Contains("Sender is null", code, StringComparison.Ordinal);
        Assert.Contains("Recipient is null", code, StringComparison.Ordinal);
        Assert.Contains("RecipientDomain is null", code, StringComparison.Ordinal);
    }

    [Fact]
    public void CollapsedDashboardRestoresFullWidthResults()
    {
        string root = RepositoryRoot();
        string code = File.ReadAllText(Path.Combine(root, "src", "MailLogInspector.App", "MainWindow.SearchSenderDashboard.cs"));

        Assert.Contains("Grid.SetColumnSpan(SearchResultsPanel, 5)", code, StringComparison.Ordinal);
        Assert.Contains("SearchResultsPanel.Margin = new Thickness(0)", code, StringComparison.Ordinal);


    }

    [Fact]
    public void OnlyFreshSearchRefreshesTheDomainDashboard()
    {
        string root = RepositoryRoot();
        string code = File.ReadAllText(Path.Combine(root, "src", "MailLogInspector.App", "MainWindow.xaml.cs"));

        Assert.Contains("RunSearchAsync(SearchRunReason.FreshSearch)", code, StringComparison.Ordinal);
        Assert.Contains("RunSearchAsync(SearchRunReason.LoadMore)", code, StringComparison.Ordinal);
        Assert.Contains("RunSearchAsync(SearchRunReason.StatusChange)", code, StringComparison.Ordinal);
        Assert.Contains("if (reason == SearchRunReason.FreshSearch)", code, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(code, "RefreshSenderDomainDashboardForFreshSearchAsync(criteria, cancellationToken)"));
    }

    [Fact]
    public void FreshSenderSearchAutomaticallyExpandsOnlySenderGroup()
    {
        string root = RepositoryRoot();
        string code = File.ReadAllText(Path.Combine(root, "src", "MailLogInspector.App", "MainWindow.xaml.cs"));

        Assert.Contains("expandSingleSenderGroup: reason == SearchRunReason.FreshSearch", code, StringComparison.Ordinal);
        Assert.Contains("criteria.Sender is not null || criteria.SenderDomain is not null", code, StringComparison.Ordinal);
        Assert.Contains("if (expandSingleSenderGroup && groups.Count == 1)", code, StringComparison.Ordinal);
        Assert.Contains("_expandedSearchGroups.Add(groups[0].GroupKey)", code, StringComparison.Ordinal);
    }
    [Fact]
    public void ExcelExportIsOpenedImmediatelyAfterSaving()
    {
        string root = RepositoryRoot();
        string code = File.ReadAllText(Path.Combine(root, "src", "MailLogInspector.App", "MainWindow.xaml.cs"));

        Assert.Contains("OpenExportedWorkbook(saveFileDialog.FileName)", code, StringComparison.Ordinal);
        Assert.Contains("UseShellExecute = true", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchExportFilenameIncludesSenderDomainAndReadableDateRange()
    {
        MethodInfo method = typeof(MailLogInspector.App.MainWindow).GetMethod(
            "BuildSearchExportFileName",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var criteria = new MailLogInspectorSearchCriteria(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 14, 23, 59, 59),
            null,
            null,
            "tpbinnenweg.nl",
            null,
            null);

        string result = Assert.IsType<string>(method.Invoke(null, [criteria]));

        Assert.Equal("mail-log-inspector-afzender-tpbinnenweg.nl-van-01-07-2026-tot-14-07-2026.xlsx", result);
    }

    [Fact]
    public void SearchExportFilenameIncludesBothAddressesAndSanitizesInvalidCharacters()
    {
        MethodInfo method = typeof(MailLogInspector.App.MainWindow).GetMethod(
            "BuildSearchExportFileName",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var criteria = new MailLogInspectorSearchCriteria(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 14, 23, 59, 59),
            "info@example.test",
            "zorg/team@example.test",
            null,
            null,
            null);

        string result = Assert.IsType<string>(method.Invoke(null, [criteria]));

        Assert.Equal("mail-log-inspector-afzender-info@example.test-ontvanger-zorg-team@example.test-van-01-07-2026-tot-14-07-2026.xlsx", result);
    }

    [Fact]
    public void SearchResultCountColumnSortsByNumericValue()
    {
        string root = RepositoryRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "MailLogInspector.App", "MainWindow.xaml"));

        Assert.Contains("Header=\"Aantal\" Width=\"62\" SortMemberPath=\"CountValue\" Binding=\"{Binding CountDisplay}\"", xaml, StringComparison.Ordinal);
    }
    private static int CountOccurrences(string value, string search)
    {
        int count = 0;
        int offset = 0;
        while ((offset = value.IndexOf(search, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += search.Length;
        }
        return count;
    }

    private static string RepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }
}
