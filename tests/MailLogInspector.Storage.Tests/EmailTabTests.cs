using MailLogInspector.App;
using MailLogInspector.Core;
using MailLogInspector.Storage;
using Xunit;

namespace MailLogInspector.Storage.Tests;

/// <summary>
/// Tests rond de E-mail-tab: melden over een zelfgekozen periode, het verzendlogboek en de
/// tabstrip die alle tabbladen bereikbaar moet houden.
/// </summary>
public sealed class EmailTabTests
{
    private static string SolutionRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static MailLogInspectorStore CreateMailStore(out string databasePath)
    {
        string root = Path.Combine(Path.GetTempPath(), "mail-log-email-tab-" + Guid.NewGuid().ToString("N"));
        databasePath = Path.Combine(root, "mail-log-inspector.sqlite");
        var store = new MailLogInspectorStore(databasePath);
        store.Initialize();
        return store;
    }

    private static BounceNotificationOperationalStore CreateNotificationStore()
    {
        string root = Path.Combine(Path.GetTempPath(), "mail-log-email-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new BounceNotificationOperationalStore(Path.Combine(root, "operational.sqlite"));
        store.Initialize();
        return store;
    }

    private static SmtpLogEntry Entry(int row, DateTime accepted, string sender, string status)
    {
        return new SmtpLogEntry(
            row,
            accepted,
            accepted.AddSeconds(30),
            sender,
            sender.Split('@')[1],
            $"ontvanger-{row}@voorbeeld.net",
            "voorbeeld.net",
            status,
            status == "D" ? "250" : "550",
            status == "D" ? "delivered" : "User unknown",
            string.Empty,
            null,
            string.Empty,
            $"tracking-{row}",
            string.Empty);
    }

    // --------------------------------------------------------------- periode

    [Fact]
    public void BounceReportOverAPeriodFindsBouncesAcrossImports()
    {
        MailLogInspectorStore store = CreateMailStore(out _);
        DateTime maandag = new(2026, 3, 2, 8, 0, 0);
        DateTime dinsdag = maandag.AddDays(1);

        store.SaveImport(
            "dag1.csv",
            "hash-dag1",
            null,
            [
                Entry(1, maandag, "verkoop@bedrijf.nl", "B"),
                Entry(2, maandag.AddMinutes(5), "verkoop@bedrijf.nl", "D")
            ],
            errorCount: 0);

        store.SaveImport(
            "dag2.csv",
            "hash-dag2",
            null,
            [
                Entry(3, dinsdag, "verkoop@bedrijf.nl", "B"),
                Entry(4, dinsdag.AddMinutes(5), "facturen@bedrijf.nl", "B")
            ],
            errorCount: 0);

        IReadOnlyList<MailLogInspectorSenderBounceReport> beideDagen =
            store.ReadSenderBounceReports(maandag.Date, dinsdag.Date);

        Assert.Equal(2, beideDagen.Count);
        MailLogInspectorSenderBounceReport verkoop =
            beideDagen.Single(report => report.SenderAddress == "verkoop@bedrijf.nl");
        Assert.Equal(2, verkoop.BounceCount);

        IReadOnlyList<MailLogInspectorSenderBounceReport> alleenMaandag =
            store.ReadSenderBounceReports(maandag.Date, maandag.Date);

        MailLogInspectorSenderBounceReport maandagVerkoop = Assert.Single(alleenMaandag);
        Assert.Equal("verkoop@bedrijf.nl", maandagVerkoop.SenderAddress);
        Assert.Equal(1, maandagVerkoop.BounceCount);
    }

    [Fact]
    public void BounceRowTimestampsSurviveTheRoundTrip()
    {
        MailLogInspectorStore store = CreateMailStore(out _);
        DateTime accepted = new(2026, 3, 2, 8, 15, 0);

        store.SaveImport(
            "tijd.csv",
            "hash-tijd",
            null,
            [Entry(1, accepted, "verkoop@bedrijf.nl", "B")],
            errorCount: 0);

        MailLogInspectorSenderBounceReport report =
            Assert.Single(store.ReadSenderBounceReports(accepted.Date, accepted.Date));

        MailLogInspectorBounceRow row = Assert.Single(report.Bounces);
        Assert.Equal(accepted, row.AcceptedAt);
    }

    [Fact]
    public void ReversedPeriodIsCorrectedInsteadOfReturningNothing()
    {
        MailLogInspectorStore store = CreateMailStore(out _);
        DateTime dag = new(2026, 3, 2, 8, 0, 0);

        store.SaveImport(
            "omgekeerd.csv",
            "hash-omgekeerd",
            null,
            [Entry(1, dag, "verkoop@bedrijf.nl", "B")],
            errorCount: 0);

        IReadOnlyList<MailLogInspectorSenderBounceReport> reports =
            store.ReadSenderBounceReports(dag.Date.AddDays(2), dag.Date);

        Assert.Single(reports);
    }

    [Fact]
    public void PeriodForImportKeepsTheReportRange()
    {
        BounceNotificationPeriod period = BounceNotificationPeriod.ForImport(
            importId: 42,
            reportStart: new DateTime(2026, 3, 1),
            reportEnd: new DateTime(2026, 3, 2),
            sourceFileName: "rapport.zip");

        Assert.Equal(BounceNotificationScope.Import, period.Scope);
        Assert.Equal(42, period.ImportId);
        Assert.Equal(new DateTime(2026, 3, 2), period.ReportDate);
        Assert.Equal("01-03-2026 t/m 02-03-2026", period.DescribePeriod());
        Assert.Contains("Import 42", period.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void PeriodForRangeNormalisesReversedDates()
    {
        BounceNotificationPeriod period = BounceNotificationPeriod.ForRange(
            new DateTime(2026, 3, 9),
            new DateTime(2026, 3, 2));

        Assert.Equal(BounceNotificationScope.Range, period.Scope);
        Assert.Null(period.ImportId);
        Assert.Equal(new DateTime(2026, 3, 2), period.FromInclusive);
        Assert.Equal(new DateTime(2026, 3, 9), period.ThroughInclusive);
    }

    [Fact]
    public void SingleDayPeriodIsDescribedAsOneDate()
    {
        BounceNotificationPeriod period = BounceNotificationPeriod.ForRange(
            new DateTime(2026, 3, 2),
            new DateTime(2026, 3, 2));

        Assert.Equal("02-03-2026", period.DescribePeriod());
    }

    // -------------------------------------------------------------- logboek

    [Fact]
    public void SentNotificationsAreRecordedInTheLog()
    {
        BounceNotificationOperationalStore store = CreateNotificationStore();
        BounceNotificationPeriod period = BounceNotificationPeriod.ForRange(
            new DateTime(2026, 3, 2),
            new DateTime(2026, 3, 2));

        store.AppendLogEntry("verkoop@bedrijf.nl", "info@bedrijf.nl", 12, period, success: true, errorMessage: null);
        store.AppendLogEntry("facturen@bedrijf.nl", "facturen@bedrijf.nl", 3, period, success: false, errorMessage: "Relay geweigerd");

        IReadOnlyList<BounceNotificationLogEntry> entries = store.ReadLogEntries();

        Assert.Equal(2, entries.Count);
        BounceNotificationLogEntry geslaagd = entries.Single(entry => entry.Success);
        Assert.Equal("verkoop@bedrijf.nl", geslaagd.SenderAddress);
        Assert.Equal("info@bedrijf.nl", geslaagd.Recipient);
        Assert.Equal(12, geslaagd.BounceCount);
        Assert.Equal("02-03-2026", geslaagd.PeriodDisplay);
        Assert.Equal("Verstuurd", geslaagd.ResultDisplay);

        BounceNotificationLogEntry mislukt = entries.Single(entry => !entry.Success);
        Assert.Contains("Relay geweigerd", mislukt.ResultDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlySuccessfulSendsCountAsAlreadyNotified()
    {
        BounceNotificationOperationalStore store = CreateNotificationStore();
        BounceNotificationPeriod period = BounceNotificationPeriod.ForRange(
            new DateTime(2026, 3, 2),
            new DateTime(2026, 3, 2));

        store.AppendLogEntry("verkoop@bedrijf.nl", "verkoop@bedrijf.nl", 4, period, success: true, errorMessage: null);
        store.AppendLogEntry("facturen@bedrijf.nl", "facturen@bedrijf.nl", 2, period, success: false, errorMessage: "Time-out");

        IReadOnlyDictionary<string, DateTime> sends =
            store.ReadSuccessfulSendsForPeriod(period.FromInclusive, period.ThroughInclusive);

        Assert.True(sends.ContainsKey("verkoop@bedrijf.nl"));
        Assert.False(sends.ContainsKey("facturen@bedrijf.nl"));
    }

    [Fact]
    public void AnotherPeriodIsNotSeenAsAlreadyNotified()
    {
        BounceNotificationOperationalStore store = CreateNotificationStore();
        BounceNotificationPeriod maandag = BounceNotificationPeriod.ForRange(
            new DateTime(2026, 3, 2),
            new DateTime(2026, 3, 2));

        store.AppendLogEntry("verkoop@bedrijf.nl", "verkoop@bedrijf.nl", 4, maandag, success: true, errorMessage: null);

        IReadOnlyDictionary<string, DateTime> dinsdag =
            store.ReadSuccessfulSendsForPeriod(new DateTime(2026, 3, 3), new DateTime(2026, 3, 3));

        Assert.Empty(dinsdag);
    }

    [Fact]
    public void LogSurvivesAnUpgradeFromADatabaseWithoutTheLogTable()
    {
        BounceNotificationOperationalStore store = CreateNotificationStore();
        store.Initialize();

        BounceNotificationPeriod period = BounceNotificationPeriod.ForRange(
            new DateTime(2026, 3, 2),
            new DateTime(2026, 3, 2));
        store.AppendLogEntry("verkoop@bedrijf.nl", "verkoop@bedrijf.nl", 1, period, success: true, errorMessage: null);

        Assert.Single(store.ReadLogEntries());
    }

    // ------------------------------------------------------------- tabstrip

    [Fact]
    public void StatusPanelLeavesRoomForEveryTab()
    {
        double? left = MainWindowTabStripLayout.CalculateStatusLeftMargin(
            tabStripWidth: 1014,
            windowWidth: 1920,
            tabControlLeftMargin: 18,
            statusRightMargin: 18);

        Assert.NotNull(left);
        Assert.Equal(18 + 1014 + MainWindowTabStripLayout.TabStripGap, left!.Value);
    }

    [Fact]
    public void StatusPanelStepsAsideWhenTheWindowIsTooNarrow()
    {
        double? left = MainWindowTabStripLayout.CalculateStatusLeftMargin(
            tabStripWidth: 1014,
            windowWidth: 1180,
            tabControlLeftMargin: 18,
            statusRightMargin: 18);

        Assert.Null(left);
    }

    [Fact]
    public void StatusPanelIsHiddenBeforeTheTabsAreMeasured()
    {
        Assert.Null(MainWindowTabStripLayout.CalculateStatusLeftMargin(0, 1920, 18, 18));
        Assert.Null(MainWindowTabStripLayout.CalculateStatusLeftMargin(1014, 0, 18, 18));
    }

    [Fact]
    public void TopStatusPanelIsPositionedFromCodeInsteadOfAFixedMargin()
    {
        string code = File.ReadAllText(Path.Combine(
            SolutionRoot(), "src", "MailLogInspector.App", "MainWindow.xaml.cs"));

        Assert.Contains("Name=\"TopStatusBorder\"", File.ReadAllText(Path.Combine(
            SolutionRoot(), "src", "MailLogInspector.App", "MainWindow.xaml")), StringComparison.Ordinal);
        Assert.Contains("UpdateTopStatusBorderPlacement", code, StringComparison.Ordinal);
        Assert.Contains("MainWindowTabStripLayout.CalculateStatusLeftMargin", code, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ e-mail-tab

    [Fact]
    public void EmailTabReplacesTheSeparateNotificationWindow()
    {
        string appDirectory = Path.Combine(SolutionRoot(), "src", "MailLogInspector.App");
        string xaml = File.ReadAllText(Path.Combine(appDirectory, "MainWindow.xaml"));

        Assert.Contains("<TabItem Name=\"EmailTab\">", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"E-mail\"", xaml, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(appDirectory, "BounceNotificationWindow.xaml")));
        Assert.False(File.Exists(Path.Combine(appDirectory, "BounceNotificationWindow.xaml.cs")));
        Assert.DoesNotContain("BounceNotificationsButton", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void EmailTabOffersPeriodChoiceSendersAndHistory()
    {
        string xaml = File.ReadAllText(Path.Combine(
            SolutionRoot(), "src", "MailLogInspector.App", "MainWindow.xaml"));

        int start = xaml.IndexOf("<TabItem Name=\"EmailTab\">", StringComparison.Ordinal);
        int end = xaml.IndexOf("<TabItem Name=\"HelpTab\">", start, StringComparison.Ordinal);
        string emailXaml = xaml[start..end];

        Assert.Contains("Name=\"EmailScopeComboBox\"", emailXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"yesterday\"", emailXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"last7\"", emailXaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"custom\"", emailXaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"EmailFromDatePicker\"", emailXaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"EmailThroughDatePicker\"", emailXaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"EmailSendersGrid\"", emailXaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"EmailHistoryGrid\"", emailXaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"EmailTransportComboBox\"", emailXaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"EmailBccAddressTextBox\"", emailXaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"EmailIncludeAttachmentCheckBox\"", emailXaml, StringComparison.Ordinal);
        Assert.Contains("PeriodStateDisplay", emailXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void EmailDatePickersUseTheSharedPopupHandlers()
    {
        string xaml = File.ReadAllText(Path.Combine(
            SolutionRoot(), "src", "MailLogInspector.App", "MainWindow.xaml"));

        int start = xaml.IndexOf("Name=\"EmailFromDatePicker\"", StringComparison.Ordinal);
        int end = xaml.IndexOf("/>", start, StringComparison.Ordinal);
        string picker = xaml[start..end];

        Assert.Contains("PreviewMouseDown=\"DatePicker_PreviewMouseDown\"", picker, StringComparison.Ordinal);
        Assert.Contains("PreviewMouseUp=\"DatePicker_PreviewMouseUp\"", picker, StringComparison.Ordinal);
        Assert.Contains("CalendarOpened=\"DatePicker_CalendarOpened\"", picker, StringComparison.Ordinal);
    }

    // ------------------------------------------------- inleiding en afsluiting

    /// <summary>
    /// De rij met instellingen bestond al voordat de tekstkolommen werden toegevoegd. Zonder
    /// terugval bleven inleiding en afsluiting daardoor voorgoed leeg in bestaande installaties.
    /// </summary>
    [Fact]
    public void LoadSettings_FallsBackToTheDefaultTextsWhenTheyWereNeverSet()
    {
        BounceNotificationOperationalStore store = CreateNotificationStore();
        store.SaveSettings(BounceNotificationSettings.Default with
        {
            Content = BounceNotificationSettings.Default.ResolveContent() with
            {
                IntroText = null,
                FooterText = null
            }
        });

        BounceNotificationContentOptions content = store.LoadSettings().ResolveContent();

        Assert.Equal(BounceNotificationContentOptions.DefaultIntroText, content.IntroText);
        Assert.Equal(BounceNotificationContentOptions.DefaultFooterText, content.FooterText);
    }

    /// <summary>
    /// Wie de tekst bewust weghaalt moet hem weg kunnen houden; anders keert de standaardtekst
    /// bij elke herstart terug.
    /// </summary>
    [Fact]
    public void LoadSettings_KeepsTextsEmptyWhenTheUserClearedThemOnPurpose()
    {
        BounceNotificationOperationalStore store = CreateNotificationStore();
        store.SaveSettings(BounceNotificationSettings.Default with
        {
            Content = BounceNotificationSettings.Default.ResolveContent() with
            {
                IntroText = string.Empty,
                FooterText = string.Empty
            }
        });

        BounceNotificationContentOptions content = store.LoadSettings().ResolveContent();

        Assert.Equal(string.Empty, content.IntroText);
        Assert.Equal(string.Empty, content.FooterText);
    }

    [Fact]
    public void DefaultIntroText_ExplainsTheReportAndUsesTheSupportedPlaceholders()
    {
        string intro = BounceNotificationContentOptions.DefaultIntroText;

        Assert.Contains("bounce", intro, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("{sender}", intro, StringComparison.Ordinal);
        Assert.Contains("{count}", intro, StringComparison.Ordinal);
        Assert.Contains("{date}", intro, StringComparison.Ordinal);
        Assert.DoesNotContain("{domain}", intro, StringComparison.Ordinal);
    }

    /// <summary>De knop is de enige weg terug nadat een tekst is weggehaald.</summary>
    [Fact]
    public void TheEmailTabOffersAButtonToRestoreTheDefaultTexts()
    {
        string xaml = File.ReadAllText(Path.Combine(
            SolutionRoot(), "src", "MailLogInspector.App", "MainWindow.xaml"));
        string code = File.ReadAllText(Path.Combine(
            SolutionRoot(), "src", "MailLogInspector.App", "MainWindow.BounceNotifications.cs"));

        Assert.Contains("Name=\"EmailResetTextsButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"EmailResetTextsButton_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("BounceNotificationContentOptions.DefaultIntroText", code, StringComparison.Ordinal);
        Assert.Contains("BounceNotificationContentOptions.DefaultFooterText", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// Stond de tab op de laatste import, dan moest de gebruiker na het laden alsnog omschakelen
    /// naar een dag en opnieuw ophalen. Gisteren als startpunt scheelt die tweede ronde.
    /// </summary>
    [Fact]
    public void TheEmailTabOpensOnYesterdayInsteadOfTheLatestImport()
    {
        string code = File.ReadAllText(Path.Combine(
            SolutionRoot(), "src", "MailLogInspector.App", "MainWindow.BounceNotifications.cs"));

        Assert.Contains("DefaultEmailScope = \"yesterday\"", code, StringComparison.Ordinal);
        Assert.Contains(
            "EmailScopeComboBox.SelectedItem is ComboBoxItem { Tag: string tag } ? tag : DefaultEmailScope",
            code,
            StringComparison.Ordinal);
        Assert.Contains("SelectComboBoxByTag(EmailScopeComboBox, DefaultEmailScope)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportStillHandsOverToTheEmailTab()
    {
        string code = File.ReadAllText(Path.Combine(
            SolutionRoot(), "src", "MailLogInspector.App", "MainWindow.BounceNotifications.cs"));

        Assert.Contains("RunBounceNotificationsAfterImportAsync", code, StringComparison.Ordinal);
        Assert.Contains("ShowEmailTabWithPlan", code, StringComparison.Ordinal);
        Assert.Contains("MainTabControl.SelectedItem = EmailTab", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new BounceNotificationWindow", code, StringComparison.Ordinal);
    }
}
