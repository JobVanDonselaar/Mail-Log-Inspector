using MailLogInspector.App;
using MailLogInspector.Core;
using MailLogInspector.Storage;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class BounceNotificationTests
{
    private static MailLogInspectorSenderBounceReport BuildReport(int bounceCount = 3)
    {
        List<MailLogInspectorBounceRow> bounces = [];
        for (int index = 0; index < bounceCount; index++)
        {
            bounces.Add(new MailLogInspectorBounceRow(
                new DateTime(2026, 2, 17, 9, 30, 0, DateTimeKind.Utc).AddMinutes(index),
                $"ontvanger{index}@voorbeeld.nl",
                MailLogInspectorReasonCode.InvalidRecipient,
                550,
                "User unknown"));
        }

        return new MailLogInspectorSenderBounceReport(
            "verzender@bedrijf.nl",
            TotalCount: 100,
            DeliveredCount: 90,
            UnderwayCount: 7,
            BounceCount: bounceCount,
            Bounces: bounces);
    }

    private static BounceNotificationOperationalStore CreateStore()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "mail-log-inspector-bounce-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var store = new BounceNotificationOperationalStore(Path.Combine(root, "operational.sqlite"));
        store.Initialize();
        return store;
    }

    [Theory]
    [InlineData("noreply@bedrijf.nl")]
    [InlineData("no-reply@bedrijf.nl")]
    [InlineData("no_reply@bedrijf.nl")]
    [InlineData("donotreply@bedrijf.nl")]
    [InlineData("do-not-reply@bedrijf.nl")]
    [InlineData("NoReply@Bedrijf.NL")]
    public void UnattendedSendersFallBackToInfoAddress(string sender)
    {
        Assert.True(MailLogInspectorNotificationAddressPolicy.IsUnattendedSender(sender));
        Assert.Equal("info@bedrijf.nl", MailLogInspectorNotificationAddressPolicy.SuggestRecipient(sender));
    }

    [Fact]
    public void NormalSenderKeepsOwnAddress()
    {
        Assert.False(MailLogInspectorNotificationAddressPolicy.IsUnattendedSender("marketing@bedrijf.nl"));
        Assert.Equal(
            "marketing@bedrijf.nl",
            MailLogInspectorNotificationAddressPolicy.SuggestRecipient("Marketing@Bedrijf.nl"));
    }

    [Theory]
    [InlineData("info@bedrijf.nl", true)]
    [InlineData("info@bedrijf", false)]
    [InlineData("info bedrijf.nl", false)]
    [InlineData("a@@b.nl", false)]
    [InlineData("", false)]
    public void PlausibilityCheckRejectsBrokenAddresses(string address, bool expected)
    {
        Assert.Equal(expected, MailLogInspectorNotificationAddressPolicy.IsPlausibleAddress(address));
    }

    [Fact]
    public void SubjectTemplateReplacesPlaceholders()
    {
        string subject = BounceNotificationContentBuilder.BuildSubject(
            "{count} bounces voor {sender} ({domain}) op {date}",
            BuildReport(),
            new DateTime(2026, 2, 17, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal("3 bounces voor verzender@bedrijf.nl (bedrijf.nl) op 17-02-2026", subject);
    }

    [Fact]
    public void NewSendersAreStoredDisabled()
    {
        BounceNotificationOperationalStore store = CreateStore();
        store.EnsureSendersExist(["verzender@bedrijf.nl", "tweede@bedrijf.nl"]);

        IReadOnlyList<BounceNotificationSender> senders = store.LoadSenders();
        Assert.Equal(2, senders.Count);
        Assert.All(senders, sender => Assert.False(sender.Enabled));
    }

    [Fact]
    public void EnableAllSwitchesEverySender()
    {
        BounceNotificationOperationalStore store = CreateStore();
        store.EnsureSendersExist(["een@bedrijf.nl", "twee@bedrijf.nl"]);

        store.SetAllSendersEnabled(true);
        Assert.All(store.LoadSenders(), sender => Assert.True(sender.Enabled));

        store.SetAllSendersEnabled(false);
        Assert.All(store.LoadSenders(), sender => Assert.False(sender.Enabled));
    }

    [Fact]
    public void NeverNotifySendersIgnoreEnableAll()
    {
        BounceNotificationOperationalStore store = CreateStore();
        store.EnsureSendersExist(["gewoon@bedrijf.nl", "demo@bedrijf.nl"]);
        store.SaveSender(new BounceNotificationSender(
            "demo@bedrijf.nl",
            Enabled: false,
            RecipientOverride: null,
            LastNotifiedAtUtc: null,
            LastNotifiedBounceCount: 0,
            NeverNotify: true));

        store.SetAllSendersEnabled(true);

        IReadOnlyList<BounceNotificationSender> senders = store.LoadSenders();
        Assert.True(senders.Single(sender => sender.SenderAddress == "gewoon@bedrijf.nl").Enabled);

        BounceNotificationSender demo = senders.Single(sender => sender.SenderAddress == "demo@bedrijf.nl");
        Assert.False(demo.Enabled);
        Assert.True(demo.NeverNotify);
    }

    [Fact]
    public void NeverNotifyBlocksSendingEvenWhenEnabled()
    {
        var item = new BounceNotificationPlanItem(
            BuildReport(),
            new BounceNotificationSender(
                "verzender@bedrijf.nl",
                Enabled: true,
                RecipientOverride: "praktijk@voorbeeld.nl",
                LastNotifiedAtUtc: null,
                LastNotifiedBounceCount: 0,
                NeverNotify: true),
            "verzender@bedrijf.nl");

        Assert.False(item.IsSendable);
    }

    [Fact]
    public void NeverNotifyRowCannotBeSwitchedOn()
    {
        var row = new BounceNotificationRowViewModel(new BounceNotificationPlanItem(
            BuildReport(),
            new BounceNotificationSender(
                "verzender@bedrijf.nl",
                Enabled: true,
                RecipientOverride: null,
                LastNotifiedAtUtc: null,
                LastNotifiedBounceCount: 0,
                NeverNotify: true),
            "praktijk@voorbeeld.nl"));

        Assert.True(row.NeverNotify);
        Assert.False(row.Enabled);

        row.Enabled = true;
        Assert.False(row.Enabled);

        row.NeverNotify = false;
        row.Enabled = true;
        Assert.True(row.Enabled);

        row.NeverNotify = true;
        Assert.False(row.Enabled);
        Assert.True(row.ToSetting().NeverNotify);
    }

    [Fact]
    public void ContentOptionsRoundTripThroughStore()
    {
        BounceNotificationOperationalStore store = CreateStore();

        var content = new BounceNotificationContentOptions(
            IncludeExcelAttachment: false,
            IncludeKpiSummary: true,
            IncludeReasonBreakdown: false,
            IncludeRecipientDomainBreakdown: false,
            IncludeDetailTable: true,
            IncludeSourceFileName: false,
            MaxDetailRows: 25,
            BodyFormat: BounceNotificationBodyFormat.TextOnly,
            IntroText: "Hallo {sender},",
            FooterText: "Groet");

        store.SaveSettings(BounceNotificationSettings.Default with
        {
            Transport = BounceNotificationTransport.SmtpRelay,
            FromAddress = "meldingen@bedrijf.nl",
            Content = content
        });

        BounceNotificationSettings loaded = store.LoadSettings();
        Assert.Equal(BounceNotificationTransport.SmtpRelay, loaded.Transport);
        Assert.Equal(content, loaded.Content);
    }

    [Fact]
    public void StoreWithoutSavedSettingsUsesFullContentDefaults()
    {
        BounceNotificationOperationalStore store = CreateStore();
        BounceNotificationContentOptions content = store.LoadSettings().ResolveContent();

        Assert.True(content.IncludeExcelAttachment);
        Assert.True(content.IncludeKpiSummary);
        Assert.True(content.IncludeDetailTable);
        Assert.Equal(BounceNotificationBodyFormat.Both, content.ResolveBodyFormat());
    }

    [Fact]
    public void DisabledBlocksAreLeftOutOfTheHtmlBody()
    {
        var content = BounceNotificationContentOptions.Default with
        {
            IncludeReasonBreakdown = false,
            IncludeRecipientDomainBreakdown = false,
            IncludeDetailTable = false,
            IncludeExcelAttachment = false
        };

        string html = BounceNotificationContentBuilder.BuildHtmlBody(
            BuildReport(),
            new DateTime(2026, 2, 17, 0, 0, 0, DateTimeKind.Utc),
            "rapport.zip",
            hasAttachment: false,
            content);

        Assert.Contains("Verzonden", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Bounce-oorzaken", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Ontvangende domeinen", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Gebouncede berichten", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Excel-bijlage", html, StringComparison.Ordinal);
    }

    [Fact]
    public void FreeTextBlocksSupportPlaceholdersAndAreEncoded()
    {
        var content = BounceNotificationContentOptions.Default with
        {
            IntroText = "Beste {sender} <team>",
            FooterText = "Vragen? {domain}"
        };

        string html = BounceNotificationContentBuilder.BuildHtmlBody(
            BuildReport(),
            new DateTime(2026, 2, 17, 0, 0, 0, DateTimeKind.Utc),
            sourceFileName: null,
            hasAttachment: false,
            content);

        Assert.Contains("Beste verzender@bedrijf.nl &lt;team&gt;", html, StringComparison.Ordinal);
        Assert.Contains("Vragen? bedrijf.nl", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Zonder eigen linkopmaak kleurt de mailclient het adres in de donkere kopbalk blauw,
    /// waardoor het onleesbaar wordt op de blauwe achtergrond.
    /// </summary>
    [Fact]
    public void SenderAddressInTheHeaderStaysReadableOnTheDarkBar()
    {
        string html = BounceNotificationContentBuilder.BuildHtmlBody(
            BuildReport(),
            new DateTime(2026, 2, 17, 0, 0, 0, DateTimeKind.Utc),
            sourceFileName: null,
            hasAttachment: false,
            BounceNotificationContentOptions.Default);

        int headerEnd = html.IndexOf("Bounce-oorzaken", StringComparison.Ordinal);
        string header = headerEnd > 0 ? html[..headerEnd] : html;

        Assert.Contains("mailto:verzender%40bedrijf.nl", header, StringComparison.Ordinal);
        Assert.Contains("color:#ffffff;text-decoration:none;", header, StringComparison.Ordinal);
    }

    [Fact]
    public void HeaderShowsExquiseTitleIconAndSentMailDateRange()
    {
        string html = BounceNotificationContentBuilder.BuildHtmlBody(
            BuildReport(),
            new DateTime(2026, 2, 17),
            sourceFileName: null,
            hasAttachment: false,
            BounceNotificationContentOptions.Default,
            fromInclusive: new DateTime(2026, 2, 10),
            throughInclusive: new DateTime(2026, 2, 17));

        Assert.Contains("Overzicht gestuurde mails vanuit Exquise Next", html, StringComparison.Ordinal);
        Assert.Contains("10-02-2026 t/m 17-02-2026", html, StringComparison.Ordinal);
        Assert.Contains(
            $"src=\"{BounceNotificationHeaderLogo.ContentSource}\"",
            html,
            StringComparison.Ordinal);
        Assert.Contains("alt=\"Exquise Next\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("&#x1F9B7;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Bounce-overzicht", html, StringComparison.Ordinal);
    }

    // ------------------------------------------------- ontvanger in het raster

    /// <summary>
    /// Leeg laten betekent "gebruik het voorstel", niet "geen ontvanger". Bleef het veld leeg
    /// staan, dan leek de melding nergens heen te gaan terwijl er wel verstuurd werd.
    /// </summary>
    [Fact]
    public void ClearingTheRecipientRestoresTheSuggestedAddress()
    {
        var row = new BounceNotificationRowViewModel(new BounceNotificationPlanItem(
            BuildReport(),
            BounceNotificationSender.CreateDisabled("verzender@bedrijf.nl"),
            "verzender@bedrijf.nl"));

        row.Recipient = "   ";

        Assert.Equal("verzender@bedrijf.nl", row.Recipient);
        Assert.Null(row.ToSetting().RecipientOverride);
    }

    /// <summary>
    /// Zonder melding aan het scherm blijft de cel leeg staan, want de bewaarde waarde is dan
    /// ongewijzigd gelijk aan het voorstel.
    /// </summary>
    [Fact]
    public void ClearingTheRecipientTellsTheScreenToRedrawTheCell()
    {
        var row = new BounceNotificationRowViewModel(new BounceNotificationPlanItem(
            BuildReport(),
            BounceNotificationSender.CreateDisabled("verzender@bedrijf.nl"),
            "verzender@bedrijf.nl"));

        var changed = new List<string?>();
        row.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        row.Recipient = string.Empty;

        Assert.Contains(nameof(BounceNotificationRowViewModel.Recipient), changed);
    }

    [Fact]
    public void AnEnteredRecipientOverridesTheSuggestion()
    {
        var row = new BounceNotificationRowViewModel(new BounceNotificationPlanItem(
            BuildReport(),
            BounceNotificationSender.CreateDisabled("verzender@bedrijf.nl"),
            "verzender@bedrijf.nl"));

        row.Recipient = " praktijk@voorbeeld.nl ";

        Assert.Equal("praktijk@voorbeeld.nl", row.Recipient);
        Assert.Equal("praktijk@voorbeeld.nl", row.ToSetting().RecipientOverride);
    }

    [Fact]
    public void DetailRowLimitTruncatesTheMailBody()
    {
        var content = BounceNotificationContentOptions.Default with
        {
            MaxDetailRows = 2,
            IncludeExcelAttachment = false
        };

        string text = BounceNotificationContentBuilder.BuildPlainTextBody(
            BuildReport(bounceCount: 5),
            new DateTime(2026, 2, 17, 0, 0, 0, DateTimeKind.Utc),
            sourceFileName: null,
            hasAttachment: false,
            content);

        Assert.Contains("ontvanger0@voorbeeld.nl", text, StringComparison.Ordinal);
        Assert.Contains("ontvanger1@voorbeeld.nl", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ontvanger2@voorbeeld.nl", text, StringComparison.Ordinal);
        Assert.Contains("eerste 2 van 5 bounces", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BodyFormatControlsWhichBodiesAreWanted()
    {
        var htmlOnly = BounceNotificationContentOptions.Default with
        {
            BodyFormat = BounceNotificationBodyFormat.HtmlOnly
        };
        Assert.True(htmlOnly.WantsHtml());
        Assert.False(htmlOnly.WantsPlainText());

        var textOnly = BounceNotificationContentOptions.Default with
        {
            BodyFormat = BounceNotificationBodyFormat.TextOnly
        };
        Assert.False(textOnly.WantsHtml());
        Assert.True(textOnly.WantsPlainText());

        Assert.True(BounceNotificationContentOptions.Default.WantsHtml());
        Assert.True(BounceNotificationContentOptions.Default.WantsPlainText());
    }

    [Fact]
    public void EmptyContentSelectionFallsBackToSummary()
    {
        var empty = new BounceNotificationContentOptions(
            IncludeExcelAttachment: false,
            IncludeKpiSummary: false,
            IncludeReasonBreakdown: false,
            IncludeRecipientDomainBreakdown: false,
            IncludeDetailTable: false,
            IncludeSourceFileName: false,
            MaxDetailRows: 0,
            BodyFormat: "onzin",
            IntroText: null,
            FooterText: null);

        BounceNotificationContentOptions resolved = empty.EnsureNotEmpty();
        Assert.True(resolved.IncludeKpiSummary);
        Assert.Equal(BounceNotificationBodyFormat.Both, resolved.ResolveBodyFormat());
        Assert.Equal(
            BounceNotificationContentOptions.DefaultMaxDetailRows,
            resolved.ResolveMaxDetailRows());
    }

    [Fact]
    public void DetailRowLimitIsCapped()
    {
        BounceNotificationContentOptions options = BounceNotificationContentOptions.Default with
        {
            MaxDetailRows = 99_999
        };

        Assert.Equal(
            BounceNotificationContentOptions.MaxDetailRowsLimit,
            options.ResolveMaxDetailRows());
    }
}
