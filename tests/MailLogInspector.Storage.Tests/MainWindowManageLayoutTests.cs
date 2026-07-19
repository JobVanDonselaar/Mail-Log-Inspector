using System;
using System.IO;
using System.Reflection;
using Xunit;
using MailLogInspector.App;
using MailLogInspector.Core;

namespace MailLogInspector.Storage.Tests;

public sealed class MainWindowManageLayoutTests
{
    private static string ReadMainWindowXaml()
    {
        string xamlPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "MailLogInspector.App", "MainWindow.xaml"));
        return File.ReadAllText(xamlPath);
    }

    private static string ReadMainWindowCode()
    {
        string codePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "MailLogInspector.App", "MainWindow.xaml.cs"));
        return File.ReadAllText(codePath);
    }
    private static string ReadAdminSettingsXaml()
    {
        string xamlPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "MailLogInspector.App", "AdminSettingsWindow.xaml"));
        return File.ReadAllText(xamlPath);
    }

    [Fact]
    public void ManageTab_UsesThreeColumnGrid()
    {
        string xaml = ReadMainWindowXaml();

        Assert.Contains("Name=\"ManageFixedColumnsGrid\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Grid Name=\"ManageMetricsGrid\" Grid.Row=\"0\" MinWidth=\"1840\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Grid Name=\"ManageFixedColumnsGrid\" Grid.Row=\"1\" MinWidth=\"1840\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ColumnDefinition Width=\"260\" />", xaml, StringComparison.Ordinal);

        int fixedGridStart = xaml.IndexOf("<Grid Name=\"ManageFixedColumnsGrid\"", StringComparison.Ordinal);
        int fixedGridColumnsEnd = xaml.IndexOf("</Grid.ColumnDefinitions>", fixedGridStart, StringComparison.Ordinal);
        string fixedGridColumns = xaml.Substring(fixedGridStart, fixedGridColumnsEnd - fixedGridStart);
        Assert.Equal(3, fixedGridColumns.Split("<ColumnDefinition", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void ManageTab_UsesCompactTopMetricsBeforeImportQualityZone()
    {
        string xaml = ReadMainWindowXaml();

        Assert.Contains("<Grid Name=\"ManageMetricsGrid\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Border Name=\"ImportQualityPanel\" Grid.Row=\"0\" Grid.Column=\"1\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"ImportDropZone\"", xaml, StringComparison.Ordinal);

        int metricsStart = xaml.IndexOf("<Grid Name=\"ManageMetricsGrid\"", StringComparison.Ordinal);
        Assert.True(metricsStart >= 0);
        int metricsEnd = xaml.IndexOf("</Grid>", metricsStart, StringComparison.Ordinal);
        Assert.True(metricsEnd > metricsStart);
        string metricsBlock = xaml.Substring(metricsStart, metricsEnd - metricsStart);

        Assert.Contains("DB grootte", metricsBlock, StringComparison.Ordinal);
        Assert.Contains("Records", metricsBlock, StringComparison.Ordinal);
        Assert.Contains("Imports", metricsBlock, StringComparison.Ordinal);
        Assert.Contains("Vanaf", metricsBlock, StringComparison.Ordinal);
        Assert.Contains("T/m", metricsBlock, StringComparison.Ordinal);
        Assert.Contains("Laatste update", metricsBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("FontFamily=\"Segoe MDL2 Assets\"", metricsBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void ManageTab_KeepsExistingFunctionalSectionsVisible()
    {
        string xaml = ReadMainWindowXaml();

        Assert.Contains("Name=\"ImportDropZone\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"SyncGmailReportsButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Acties en opslag\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"Archieven en opslag\" Style=\"{StaticResource SectionTitleStyle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"MonthArchiveGrid\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"ImportsGrid\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"Kernstatus\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ManageTab_UsesVariantBImportQualityMeasureCards()
    {
        string xaml = ReadMainWindowXaml();

        Assert.Contains("Name=\"ImportQualityPanel\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Name=\"ImportQualityStatusMixGrid\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"ImportQualityBounceCauseItemsControl\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"ImportQualityMeasureCardsItemsControl\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Totalen: laatste import vs vorige week", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding LatestDisplay}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding PreviousWeekDisplay}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding PreviousWeekBarHeight}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding LatestBarHeight}", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("{Binding AverageDisplay}", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Geen vergelijkbasis", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Name=\"ImportQualityOverlayBarsItemsControl\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Name=\"ImportQualityAcceptedScaleTextBlock\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Name=\"ImportQualityBounceScaleTextBlock\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Name=\"DeliveredTrendPolyline\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Name=\"BounceTrendPolyline\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Name=\"UnderwayTrendPolyline\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ManageTab_RemovesDuplicateImportQualitySummaryLines()
    {
        string xaml = ReadMainWindowXaml();
        string code = ReadMainWindowCode();

        Assert.DoesNotContain("Name=\"ImportQualityTrendSummaryTextBlock\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Name=\"ImportQualityLatestSummaryTextBlock\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ImportQualityTrendSummaryTextBlock", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ImportQualityLatestSummaryTextBlock", code, StringComparison.Ordinal);
    }
    [Fact]
    public void ManageTab_UsesOneCombinedImportsTile()
    {
        string xaml = ReadMainWindowXaml();

        Assert.Contains("Name=\"ImportsPanel\" Grid.Row=\"1\" Grid.RowSpan=\"2\" Grid.Column=\"0\" Grid.ColumnSpan=\"2\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Imports\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"ImportsGrid\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Datum\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Bron\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Bestand\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Rapportperiode\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Mails\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Afgeleverd\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Bounce\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Onderweg\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Status\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ManageTab_RemovesGmailSettingsAndHistoryFromNormalWindow()
    {
        string xaml = ReadMainWindowXaml();

        Assert.DoesNotContain("Name=\"GmailSettingsPanel\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Name=\"GmailHistoryGrid\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Name=\"GmailAuthModeComboBox\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Name=\"GmailAccountTextBox\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Name=\"GmailAppPasswordBox\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"Gmail instellingen\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"Gmail historie\"", xaml, StringComparison.Ordinal);
    }
    [Fact]
    public void ManageTab_KeepsManualSyncAction()
    {
        string xaml = ReadMainWindowXaml();

        Assert.Contains("Name=\"SyncGmailReportsButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Sync nu\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ManageTab_ShowsStopActionOnlyDuringSynchronization()
    {
        string xaml = ReadMainWindowXaml();

        Assert.Contains("Name=\"SyncCancelButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"Collapsed\"", xaml[xaml.IndexOf("Name=\"SyncCancelButton\"", StringComparison.Ordinal)..], StringComparison.Ordinal);
        Assert.Contains("Click=\"SyncCancelButton_Click\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ManageTab_UsesVariantBFixedColumnPlacement()
    {
        string xaml = ReadMainWindowXaml();

        Assert.Contains("Name=\"DeliveredThirtyDayTrendPanel\" Grid.Row=\"0\" Grid.Column=\"0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"DeliveredThirtyDayAverageTextBlock\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"DeliveredThirtyDayLowestTextBlock\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"ImportsPanel\" Grid.Row=\"1\" Grid.RowSpan=\"2\" Grid.Column=\"0\" Grid.ColumnSpan=\"2\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"ImportQualityPanel\" Grid.Row=\"0\" Grid.Column=\"1\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"ActionsStoragePanel\" Grid.Row=\"1\" Grid.RowSpan=\"2\" Grid.Column=\"2\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Name=\"GmailSettingsPanel\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Name=\"ArchiveStoragePanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Label}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"Normaal\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DeliveryLatencyPanel_UsesCompactAxesWithoutLatencyMarker()
    {
        string xaml = ReadMainWindowXaml();

        Assert.Contains("Afleversnelheid laatste 30 dagen", xaml, StringComparison.Ordinal);
        Assert.Contains("Gem. aflevertijd", xaml, StringComparison.Ordinal);
        Assert.Contains("95% binnen", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"DeliveredThirtyDayYAxisMaximumTextBlock\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"DeliveredThirtyDayYAxisMidpointTextBlock\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DayAxisLabel", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("LatencyBrush", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void BounceCausePanel_DistributesBarsAcrossAvailableHeight()
    {
        string xaml = ReadMainWindowXaml();

        Assert.Contains("Name=\"ImportQualityBounceCauseItemsControl\" VerticalContentAlignment=\"Stretch\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<UniformGrid Columns=\"1\" />", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DeliveryLatencyTrend_CalculatesLatestAverageAndP95Band()
    {
        IReadOnlyList<MailLogInspectorDeliveryLatencyDay> days = new[]
        {
            new MailLogInspectorDeliveryLatencyDay(new DateTime(2026, 7, 8), 80, 80, 4800, 0, 60, 78, 80, 80),
            new MailLogInspectorDeliveryLatencyDay(new DateTime(2026, 7, 9), 90, 90, 8100, 0, 40, 80, 88, 90),
            new MailLogInspectorDeliveryLatencyDay(new DateTime(2026, 7, 10), 100, 100, 12000, 0, 50, 90, 97, 100),
        };

        MethodInfo? method = typeof(MainWindow).GetMethod("BuildDeliveryLatencyTrend", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        object result = method.Invoke(null, new object[] { days, false })!;
        Assert.Equal(100, ReadIntProperty(result, "LatestDelivered"));
        Assert.Equal("2m 00s", ReadStringProperty(result, "LatestAverageDurationDisplay"));
        Assert.Equal("15 min", ReadStringProperty(result, "LatestP95Display"));
        Assert.Equal("100", ReadStringProperty(result, "YAxisMaximumDisplay"));
        Assert.Equal("50", ReadStringProperty(result, "YAxisMidpointDisplay"));
        Assert.Contains("binnen 5 min", ReadStringProperty(result, "SummaryText"), StringComparison.Ordinal);
    }
    [Fact]
    public void ImportQualityComparison_ShowsNoShadowWhenExactPreviousWeekIsMissing()
    {
        string xaml = ReadMainWindowXaml();
        IReadOnlyList<MailLogInspectorImportedFile> imports = new[]
        {
            CreateImport(1, importedAt: new DateTime(2026, 7, 18, 3, 0, 0), reportStart: new DateTime(2026, 7, 16, 4, 0, 0), reportEnd: new DateTime(2026, 7, 17, 23, 0, 0), rows: 100, delivered: 90, bounced: 5)
        };

        MethodInfo method = typeof(MainWindow).GetMethod("BuildImportQualityComparisonGroups", BindingFlags.NonPublic | BindingFlags.Static)!;
        object result = method.Invoke(null, new object[] { imports })!;
        object acceptedBar = ReadFirstBar(result);

        Assert.False(ReadBoolProperty(result, "HasPreviousWeek"));
        Assert.Equal("Geen gegevens vorige week", ReadStringProperty(acceptedBar, "PreviousWeekDisplay"));
        Assert.Equal(0.0, ReadDoubleProperty(acceptedBar, "PreviousWeekBarHeight"));
        Assert.DoesNotContain("Geen vergelijkbasis", xaml, StringComparison.Ordinal);
    }
    [Fact]
    public void ImportQualityComparison_UsesOnlyExactPreviousWeekDailyImports()
    {
        IReadOnlyList<MailLogInspectorImportedFile> imports = new[]
        {
            CreateImport(1, importedAt: new DateTime(2026, 7, 18, 3, 0, 0), reportStart: new DateTime(2026, 7, 16, 4, 0, 0), reportEnd: new DateTime(2026, 7, 17, 23, 0, 0), rows: 100, delivered: 90, bounced: 5),
            CreateImport(2, importedAt: new DateTime(2026, 7, 11, 3, 0, 0), reportStart: new DateTime(2026, 7, 9, 4, 0, 0), reportEnd: new DateTime(2026, 7, 10, 23, 0, 0), rows: 300, delivered: 270, bounced: 15),
            CreateImport(5, importedAt: new DateTime(2026, 7, 11, 2, 0, 0), reportStart: new DateTime(2026, 7, 9, 4, 0, 0), reportEnd: new DateTime(2026, 7, 10, 23, 0, 0), rows: 300, delivered: 270, bounced: 15),
            CreateImport(3, importedAt: new DateTime(2026, 7, 4, 3, 0, 0), reportStart: new DateTime(2026, 7, 2, 4, 0, 0), reportEnd: new DateTime(2026, 7, 3, 23, 0, 0), rows: 900, delivered: 800, bounced: 50),
            CreateImport(4, importedAt: new DateTime(2026, 7, 11, 4, 0, 0), reportStart: new DateTime(2026, 6, 1, 0, 0, 0), reportEnd: new DateTime(2026, 7, 10, 23, 0, 0), rows: 10000, delivered: 9000, bounced: 500)
        };

        MethodInfo method = typeof(MainWindow).GetMethod("BuildImportQualityComparisonGroups", BindingFlags.NonPublic | BindingFlags.Static)!;
        object result = method.Invoke(null, new object[] { imports })!;
        object acceptedBar = ReadFirstBar(result);

        Assert.True(ReadBoolProperty(result, "HasPreviousWeek"));
        Assert.Equal(300, ReadIntProperty(result, "PreviousWeekAccepted"));
        Assert.Equal(270, ReadIntProperty(result, "PreviousWeekDelivered"));
        Assert.Equal(15, ReadIntProperty(result, "PreviousWeekBounce"));
        Assert.Equal("Vorige week 300", ReadStringProperty(acceptedBar, "PreviousWeekDisplay"));
        Assert.True(ReadDoubleProperty(acceptedBar, "PreviousWeekBarHeight") > 0);
    }
    [Fact]
    public void ManageTab_KeepsSyncActionAndMovesFixedSettingsToAdminWindow()
    {
        string xaml = ReadMainWindowXaml();
        string code = ReadMainWindowCode();
        string adminXaml = ReadAdminSettingsXaml();

        Assert.Contains("Name=\"ManageActionsButtonGrid\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"SyncGmailReportsButton\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Name=\"GmailAutoSyncCheckBox\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Name=\"CloseToTrayCheckBox\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("GmailAutoSyncIntervalComboBox", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"AdminAutoSyncCheckBox\"", adminXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Automatisch synchroniseren - elke 15 min\"", adminXaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"AdminCloseToTrayCheckBox\" Content=\"Sluiten naar systeemvak\"", adminXaml, StringComparison.Ordinal);
        Assert.Contains("FixedGmailAutoSyncIntervalMinutes = 15", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadGmailAutoSyncIntervalMinutes", code, StringComparison.Ordinal);
    }

    private static MailLogInspectorImportedFile CreateImport(long id, DateTime importedAt, DateTime reportStart, DateTime reportEnd, int rows, int delivered, int bounced)
    {
        return new MailLogInspectorImportedFile(id, $"C:\\Temp\\report-{id}.zip", $"report-{id}.zip", $"hash-{id}", importedAt, reportStart, reportEnd, rows, null, delivered, bounced, rows - delivered - bounced);
    }

    private static string ReadStringProperty(object instance, string propertyName)
    {
        return (string)instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)!.GetValue(instance)!;
    }
    private static int ReadIntProperty(object instance, string propertyName)
    {
        return (int)instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)!.GetValue(instance)!;
    }

    private static bool ReadBoolProperty(object instance, string propertyName)
    {
        return (bool)instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)!.GetValue(instance)!;
    }

    private static double ReadDoubleProperty(object instance, string propertyName)
    {
        return (double)instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)!.GetValue(instance)!;
    }

    private static object ReadFirstBar(object comparison)
    {
        var bars = (System.Collections.IEnumerable)comparison.GetType().GetProperty("Bars", BindingFlags.Public | BindingFlags.Instance)!.GetValue(comparison)!;
        return bars.Cast<object>().First();
    }
}
