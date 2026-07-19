using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class MainWindowLayoutConsistencyTests
{
    [Fact]
    public void SearchAndAnalysisShareTheSameLeftFilterGrid()
    {
        string xamlPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "MailLogInspector.App", "MainWindow.xaml"));
        string xaml = File.ReadAllText(xamlPath);

        Regex sharedLeftFilterGrid = new(
            "<Grid Grid\\.Column=\"0\">\\s*<Grid\\.ColumnDefinitions>\\s*<ColumnDefinition Width=\"120\" />\\s*<ColumnDefinition Width=\"120\" />\\s*<ColumnDefinition Width=\"212\" />\\s*<ColumnDefinition Width=\"212\" />\\s*</Grid\\.ColumnDefinitions>",
            RegexOptions.CultureInvariant);

        Assert.Equal(2, sharedLeftFilterGrid.Matches(xaml).Count);
    }
    [Fact]
    public void AnalysisTabOmitsStatusFilterAndOffersLargeTopLimits()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string xaml = File.ReadAllText(Path.Combine(root, "src", "MailLogInspector.App", "MainWindow.xaml"));
        string code = File.ReadAllText(Path.Combine(root, "src", "MailLogInspector.App", "MainWindow.xaml.cs"));

        Assert.DoesNotContain("AnalysisStatusComboBox", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AnalysisStatusComboBox", code, StringComparison.Ordinal);
        Assert.Contains("Name=\"AnalysisTopDomainLimitComboBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<ComboBoxItem Content=\"100\" Tag=\"100\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<ComboBoxItem Content=\"200\" Tag=\"200\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<ComboBoxItem Content=\"500\" Tag=\"500\" />", xaml, StringComparison.Ordinal);
    }
    [Fact]
    public void HelpTabDescribesAllCurrentUserWorkflows()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string xaml = File.ReadAllText(Path.Combine(root, "src", "MailLogInspector.App", "MainWindow.xaml"));
        int helpStart = xaml.IndexOf("<TabItem Name=\"HelpTab\">", StringComparison.Ordinal);
        int helpEnd = xaml.IndexOf("</TabItem>", helpStart, StringComparison.Ordinal);
        string helpXaml = xaml[helpStart..helpEnd];

        Assert.Contains("Text=\"Help\"", helpXaml, StringComparison.Ordinal);
        Assert.Contains("Doel en snel beginnen", helpXaml, StringComparison.Ordinal);
        Assert.Contains("Zoeken", helpXaml, StringComparison.Ordinal);
        Assert.Contains("Domeinanalyse en Excel", helpXaml, StringComparison.Ordinal);
        Assert.Contains("Analyse", helpXaml, StringComparison.Ordinal);
        Assert.Contains("Dashboard", helpXaml, StringComparison.Ordinal);
        Assert.Contains("Synchronisatie en beheerdersinstellingen", helpXaml, StringComparison.Ordinal);
        Assert.Contains("Database en maandarchieven", helpXaml, StringComparison.Ordinal);
        Assert.Contains("Systeemvak en afsluiten", helpXaml, StringComparison.Ordinal);
        Assert.Contains("Veelvoorkomende situaties", helpXaml, StringComparison.Ordinal);
        Assert.Contains("Resultaten", helpXaml, StringComparison.Ordinal);
        Assert.Contains("Meer laden", helpXaml, StringComparison.Ordinal);
        Assert.Contains("Domeinanalyse tonen", helpXaml, StringComparison.Ordinal);
        Assert.Contains("SMTP-responsen", helpXaml, StringComparison.Ordinal);
        Assert.Contains("Nu synchroniseren", helpXaml, StringComparison.Ordinal);
        Assert.Contains("Proefdownload", helpXaml, StringComparison.Ordinal);
        Assert.Contains("Standaardsyntax", helpXaml, StringComparison.Ordinal);
        Assert.Contains("Maandarchieven", helpXaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"OpenAdminSettingsButton\"", helpXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Image", helpXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Assets/Help", helpXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void BusinessHelpUsesVersion0197()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string project = File.ReadAllText(Path.Combine(root, "src", "MailLogInspector.App", "MailLogInspector.App.csproj"));
        string version = File.ReadAllText(Path.Combine(root, "src", "MailLogInspector.App", "MailLogInspectorVersion.cs"));

        Assert.Contains("<InformationalVersion>0.197</InformationalVersion>", project, StringComparison.Ordinal);
        Assert.Contains("SemanticVersion = \"0.197\"", version, StringComparison.Ordinal);
    }

    [Fact]
    public void BusinessHelpOmitsDeprecatedWarningCallouts()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string helpXaml = File.ReadAllText(Path.Combine(root, "src", "MailLogInspector.App", "MainWindow.xaml"));

        Assert.DoesNotContain("Beheerdersinstellingen wijzigen de synchronisatiebron", helpXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Wijzig of vervang SQLite-bestanden nooit handmatig", helpXaml, StringComparison.Ordinal);
    }
    [Fact]
    public void AnalysisDomainNavigationReactivatesSearchAfterApplyingFilters()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string code = File.ReadAllText(Path.Combine(root, "src", "MailLogInspector.App", "MainWindow.xaml.cs"));
        int methodStart = code.IndexOf("private async Task OpenDomainInSearchAsync", StringComparison.Ordinal);
        int methodEnd = code.IndexOf("\n\tprivate int ReadSearchLimit", methodStart, StringComparison.Ordinal);
        string method = code[methodStart..methodEnd];

        Assert.Contains("Dispatcher.BeginInvoke", method, StringComparison.Ordinal);
        Assert.Contains("MainTabControl.SelectedIndex = 0", method, StringComparison.Ordinal);
        Assert.True(method.IndexOf("SenderTextBox.Text", StringComparison.Ordinal) < method.LastIndexOf("MainTabControl.SelectedIndex = 0", StringComparison.Ordinal));
    }

    [Fact]
    public void StartupShowsVisibleProgressOverlayUntilInitializationCompletes()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string xaml = File.ReadAllText(Path.Combine(root, "src", "MailLogInspector.App", "MainWindow.xaml"));
        string code = File.ReadAllText(Path.Combine(root, "src", "MailLogInspector.App", "MainWindow.xaml.cs"));

        Assert.Contains("Name=\"StartupOverlay\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"StartupProgressBar\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"StartupStatusTextBlock\"", xaml, StringComparison.Ordinal);
        Assert.Contains("StartupOverlay.Visibility = Visibility.Collapsed", code, StringComparison.Ordinal);
        Assert.Contains("StartupStatusTextBlock.Text", code, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupRunsDatabasePreparationOffTheUiThread()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string code = File.ReadAllText(Path.Combine(root, "src", "MailLogInspector.App", "MainWindow.xaml.cs"));

        Assert.Contains("await Task.Run(() => _rebuilder.RebuildIfRequiredAsync", code, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupHidesOverlayBeforeRunningOptionalMaintenance()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string code = File.ReadAllText(Path.Combine(root, "src", "MailLogInspector.App", "MainWindow.xaml.cs"));
        int loadedStart = code.IndexOf("base.Loaded +=", StringComparison.Ordinal);
        int constructorEnd = code.IndexOf("private async void SearchButton_Click", loadedStart, StringComparison.Ordinal);
        string loadedFlow = code[loadedStart..constructorEnd];

        Assert.Contains("RunPostStartupMaintenanceAsync", loadedFlow, StringComparison.Ordinal);
        Assert.True(
            loadedFlow.IndexOf("StartupOverlay.Visibility = Visibility.Collapsed", StringComparison.Ordinal) <
            loadedFlow.IndexOf("RunPostStartupMaintenanceAsync", StringComparison.Ordinal));
    }
}
