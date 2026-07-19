using MailLogInspector.App;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class SmtpPortalReportNameSyntaxTests
{
    [Fact]
    public void DefaultTemplate_MatchesCurrentReportName()
    {
        const string reportName =
            "NextGen_2026-07-17(00)_2026-07-18(00) (delivered + bounced + queue) (raw_event_stream)";

        Assert.Equal(
            "NextGen_{start}(00)_{end}(00) (delivered + bounced + queue) (raw_event_stream)",
            SmtpPortalReportNameSyntax.DefaultTemplate);
        Assert.True(SmtpPortalReportMatcher.TryParse(
            reportName,
            "Ready",
            SmtpPortalReportNameSyntax.DefaultTemplate,
            out SmtpPortalReport? report));
        Assert.NotNull(report);
    }

    [Fact]
    public void Validate_AcceptsExactlyOneStartAndEndPlaceholder()
    {
        SmtpPortalReportSyntaxValidation validation =
            SmtpPortalReportNameSyntax.Validate("Exquise_{start}_{end}_dagrapport");

        Assert.True(validation.IsValid);
        Assert.Null(validation.ErrorMessage);
    }

    [Theory]
    [InlineData("", "Vul een rapportsyntax in.")]
    [InlineData("Exquise_{end}", "Gebruik {start} exact één keer.")]
    [InlineData("Exquise_{start}", "Gebruik {end} exact één keer.")]
    [InlineData("Exquise_{start}_{start}_{end}", "Gebruik {start} exact één keer.")]
    [InlineData("Exquise_{start}_{end}_{end}", "Gebruik {end} exact één keer.")]
    [InlineData("Exquise_{start}_{end}_{date}", "Onbekende placeholder: {date}.")]
    [InlineData("Exquise_{start}_{end", "Gebruik geldige accolades rond placeholders.")]
    public void Validate_RejectsInvalidTemplates(string template, string expectedMessage)
    {
        SmtpPortalReportSyntaxValidation validation =
            SmtpPortalReportNameSyntax.Validate(template);

        Assert.False(validation.IsValid);
        Assert.Equal(expectedMessage, validation.ErrorMessage);
    }

    [Fact]
    public void Validate_RejectsTemplateLongerThanThreeHundredCharacters()
    {
        string template = new string('x', 290) + "{start}_{end}";

        SmtpPortalReportSyntaxValidation validation =
            SmtpPortalReportNameSyntax.Validate(template);

        Assert.False(validation.IsValid);
        Assert.Equal("De rapportsyntax mag maximaal 300 tekens bevatten.", validation.ErrorMessage);
    }

    [Fact]
    public void BuildRegex_TreatsAllNonPlaceholderCharactersLiterally()
    {
        const string template = "Exquise.[{start}] + ({end})";
        var regex = SmtpPortalReportNameSyntax.BuildRegex(template);

        Assert.Matches(regex, "Exquise.[2026-07-17] + (2026-07-18)");
        Assert.DoesNotMatch(regex, "Exquisex[2026-07-17]   (2026-07-18)");
    }

    [Fact]
    public void BuildExample_UsesReadableFixedDates()
    {
        string example = SmtpPortalReportNameSyntax.BuildExample(
            "Exquise_{start}_tot_{end}");

        Assert.Equal("Exquise_2026-07-17_tot_2026-07-18", example);
    }

    [Fact]
    public void ResolveTemplate_UsesDefaultOrValidatedCustomTemplate()
    {
        Assert.Equal(
            SmtpPortalReportNameSyntax.DefaultTemplate,
            SmtpPortalReportNameSyntax.ResolveTemplate(true, "ignored"));
        Assert.Equal(
            "Exquise_{start}_{end}",
            SmtpPortalReportNameSyntax.ResolveTemplate(false, "  Exquise_{start}_{end}  "));
        Assert.Throws<InvalidOperationException>(() =>
            SmtpPortalReportNameSyntax.ResolveTemplate(false, "Exquise_{start}"));
    }

    [Fact]
    public void MatcherAndSelection_UseCustomTemplate()
    {
        const string template = "Exquise-{start}-tot-{end}-dagrapport.zip";
        const string customName = "Exquise-2026-07-17-tot-2026-07-18-dagrapport.zip";
        SmtpPortalReportRow[] rows =
        [
            new(customName, "Ready", "custom"),
            new(
                "NextGen_2026-07-18(00)_2026-07-19(00) (delivered + bounced + queue) (raw_event_stream)",
                "Ready",
                "default")
        ];

        Assert.True(SmtpPortalReportMatcher.TryParse(
            customName,
            "Ready",
            template,
            out SmtpPortalReport? parsed));
        Assert.NotNull(parsed);
        Assert.False(SmtpPortalReportMatcher.TryParse(
            customName,
            "Processing",
            template,
            out _));
        Assert.False(SmtpPortalReportMatcher.TryParse(
            "Exquise-2026-07-18-tot-2026-07-17-dagrapport.zip",
            "Ready",
            template,
            out _));

        SmtpPortalReport newest = SmtpPortalReportMatcher.SelectNewest(rows, template);
        IReadOnlyList<SmtpPortalReport> required = SmtpPortalReportMatcher.SelectRequired(
            rows,
            null,
            new DateTime(2026, 7, 18),
            latestOnly: true,
            template);

        Assert.Equal(customName, newest.Name);
        Assert.Equal(customName, Assert.Single(required).Name);
    }
}
