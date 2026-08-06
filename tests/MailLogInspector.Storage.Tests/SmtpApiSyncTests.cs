using MailLogInspector.App;
using MailLogInspector.Storage;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class SmtpApiSyncTests
{
    private const string DefaultTemplate =
        "NextGen_{start}(00)_{end}(00) (delivered + bounced + queue) (raw_event_stream)";

    [Fact]
    public void StoreRoundTripsConfig()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "mail-log-inspector-api-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new SmtpApiOperationalStore(Path.Combine(root, "operational.sqlite"));
        store.Initialize();

        Assert.False(store.LoadConfig().HasApiKey);

        store.SaveConfig(SmtpApiConfig.Empty with
        {
            EncryptedApiKey = "encrypted",
            Channel = "NextGen",
            ReportSyntax1 = DefaultTemplate,
            ReportSyntax2 = "Alt_{start}_{end}",
            ConnectionStatus = "Getest"
        });
        SmtpApiConfig loaded = store.LoadConfig();

        Assert.True(loaded.HasApiKey);
        Assert.Equal("NextGen", loaded.Channel);
        Assert.Equal(DefaultTemplate, loaded.ReportSyntax1);
        Assert.Equal("Alt_{start}_{end}", loaded.ReportSyntax2);
        Assert.Null(loaded.ReportSyntax3);
        Assert.Null(loaded.LastSuccessfulUseAtUtc);

        var usedAt = new DateTime(2026, 8, 6, 5, 0, 0, DateTimeKind.Utc);
        store.RecordSuccessfulUse(usedAt);

        SmtpApiConfig afterUse = store.LoadConfig();
        Assert.Equal(usedAt, afterUse.LastSuccessfulUseAtUtc);
        Assert.Equal("encrypted", afterUse.EncryptedApiKey);
    }

    [Fact]
    public void ResolveKeepsOrderAndSkipsEmptyAndInvalidSlots()
    {
        IReadOnlyList<string> resolved = SmtpApiReportSyntaxSet.Resolve(
            DefaultTemplate,
            "   ",
            "Zonder placeholders");

        Assert.Equal([DefaultTemplate], resolved);
    }

    [Fact]
    public void ResolveDeduplicatesAndFallsBackToDefault()
    {
        Assert.Equal(
            [SmtpPortalReportNameSyntax.DefaultTemplate],
            SmtpApiReportSyntaxSet.Resolve(null, "", "   "));

        IReadOnlyList<string> resolved = SmtpApiReportSyntaxSet.Resolve(
            "A_{start}_{end}",
            "A_{start}_{end}",
            "B_{start}_{end}");
        Assert.Equal(["A_{start}_{end}", "B_{start}_{end}"], resolved);
    }

    [Fact]
    public void ValidateReportsSlotNumberOfFirstInvalidTemplate()
    {
        SmtpApiReportSyntaxSetValidation validation = SmtpApiReportSyntaxSet.Validate(
            DefaultTemplate,
            "geen placeholders",
            null);

        Assert.False(validation.IsValid);
        Assert.Equal(2, validation.SlotNumber);
        Assert.Contains("Syntax 2", validation.ErrorMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRequiresAtLeastOneTemplate()
    {
        SmtpApiReportSyntaxSetValidation validation = SmtpApiReportSyntaxSet.Validate(null, "", "  ");

        Assert.False(validation.IsValid);
        Assert.Contains("minimaal", validation.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseAvailableIgnoresPendingReportsAndOtherChannels()
    {
        SmtpApiReport[] reports =
        [
            CreateReport("a", ReportName("2026-08-05", "2026-08-06"), "done", "NextGen"),
            CreateReport("b", ReportName("2026-08-04", "2026-08-05"), "pending", "NextGen"),
            CreateReport("c", ReportName("2026-08-03", "2026-08-04"), "done", "Exquise")
        ];

        IReadOnlyList<SmtpApiSelectedReport> parsed = SmtpApiReportMatcher.ParseAvailable(
            reports,
            [DefaultTemplate],
            "NextGen");

        SmtpApiSelectedReport single = Assert.Single(parsed);
        Assert.Equal("a", single.ReportId);
        Assert.Equal(new DateTime(2026, 8, 5), single.PeriodStart);
        Assert.Equal(1, single.SyntaxSlot);
    }

    [Fact]
    public void ParseAvailableAcceptsAllChannelsWhenChannelIsEmpty()
    {
        SmtpApiReport[] reports =
        [
            CreateReport("a", ReportName("2026-08-05", "2026-08-06"), "done", "NextGen"),
            CreateReport("c", ReportName("2026-08-03", "2026-08-04"), "done", "Exquise")
        ];

        Assert.Equal(2, SmtpApiReportMatcher.ParseAvailable(reports, [DefaultTemplate], null).Count);
    }

    [Fact]
    public void SelectRequiredReturnsOnlyNewestWhenLatestOnly()
    {
        SmtpApiReport[] reports =
        [
            CreateReport("a", ReportName("2026-08-05", "2026-08-06"), "done", "NextGen"),
            CreateReport("b", ReportName("2026-08-04", "2026-08-05"), "done", "NextGen")
        ];

        IReadOnlyList<SmtpApiSelectedReport> required = SmtpApiReportMatcher.SelectRequired(
            reports,
            [DefaultTemplate],
            "NextGen",
            latestReportDay: null,
            yesterday: new DateTime(2026, 8, 6),
            latestOnly: true);

        Assert.Equal("a", Assert.Single(required).ReportId);
    }

    [Fact]
    public void SelectRequiredReturnsOnlyDaysAfterLatestImport()
    {
        SmtpApiReport[] reports =
        [
            CreateReport("a", ReportName("2026-08-05", "2026-08-06"), "done", "NextGen"),
            CreateReport("b", ReportName("2026-08-04", "2026-08-05"), "done", "NextGen"),
            CreateReport("c", ReportName("2026-08-03", "2026-08-04"), "done", "NextGen"),
            CreateReport("d", ReportName("2026-08-06", "2026-08-07"), "done", "NextGen")
        ];

        IReadOnlyList<SmtpApiSelectedReport> required = SmtpApiReportMatcher.SelectRequired(
            reports,
            [DefaultTemplate],
            "NextGen",
            latestReportDay: new DateTime(2026, 8, 3),
            yesterday: new DateTime(2026, 8, 5),
            latestOnly: false);

        Assert.Equal(["b", "a"], required.Select(report => report.ReportId).ToArray());
    }

    [Fact]
    public void SelectRequiredMatchesSecondSyntaxSlot()
    {
        SmtpApiReport[] reports =
        [
            CreateReport("alt", "Alt_2026-08-05_2026-08-06", "done", "NextGen")
        ];

        IReadOnlyList<SmtpApiSelectedReport> required = SmtpApiReportMatcher.SelectRequired(
            reports,
            [DefaultTemplate, "Alt_{start}_{end}"],
            "NextGen",
            latestReportDay: null,
            yesterday: new DateTime(2026, 8, 6),
            latestOnly: true);

        SmtpApiSelectedReport single = Assert.Single(required);
        Assert.Equal("alt", single.ReportId);
        Assert.Equal(2, single.SyntaxSlot);
    }

    [Fact]
    public void BuildThrowsForInvalidSyntaxAndKeepsStoredKeyOnPlaceholder()
    {
        SmtpApiConfig stored = SmtpApiConfig.Empty with { EncryptedApiKey = "encrypted" };

        Assert.Throws<InvalidOperationException>(() => SmtpApiAdminConfigBuilder.Build(
            stored,
            new SmtpApiAdminSettingsInput("", "", "geen placeholders", "", "")));

        SmtpApiConfig result = SmtpApiAdminConfigBuilder.Build(
            stored,
            new SmtpApiAdminSettingsInput(
                SmtpApiAdminConfigBuilder.StoredSecretPlaceholder,
                " NextGen ",
                DefaultTemplate,
                "",
                ""));

        Assert.Equal("encrypted", result.EncryptedApiKey);
        Assert.Equal("NextGen", result.Channel);
        Assert.Equal(DefaultTemplate, result.ReportSyntax1);
        Assert.Null(result.ReportSyntax2);
    }

    private static string ReportName(string start, string end)
    {
        return $"NextGen_{start}(00)_{end}(00) (delivered + bounced + queue) (raw_event_stream)";
    }

    private static SmtpApiReport CreateReport(string id, string name, string status, string channel)
    {
        return new SmtpApiReport(
            id,
            name,
            status,
            $"https://example.test/{id}.zip",
            channel,
            new DateTime(2026, 8, 6, 4, 0, 0, DateTimeKind.Utc));
    }
}
