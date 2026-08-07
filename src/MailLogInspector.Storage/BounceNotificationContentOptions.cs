namespace MailLogInspector.Storage;

/// <summary>Welke bodyvorm de melding krijgt.</summary>
public static class BounceNotificationBodyFormat
{
    /// <summary>HTML met een platte tekst-alternatief.</summary>
    public const string Both = "both";

    /// <summary>Alleen opgemaakte HTML.</summary>
    public const string HtmlOnly = "html";

    /// <summary>Alleen platte tekst.</summary>
    public const string TextOnly = "text";

    public const string Default = Both;

    public static string Normalize(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            HtmlOnly => HtmlOnly,
            TextOnly => TextOnly,
            _ => Both
        };
    }

    public static string Describe(string? value)
    {
        return Normalize(value) switch
        {
            HtmlOnly => "Alleen HTML",
            TextOnly => "Alleen platte tekst",
            _ => "HTML met tekstalternatief"
        };
    }
}

/// <summary>
/// Bepaalt welke onderdelen in een bouncemelding terechtkomen. Elk blok kan los aan of uit,
/// zodat de gebruiker kiest tussen een korte samenvatting en een volledig rapport.
/// </summary>
public sealed record BounceNotificationContentOptions(
    bool IncludeExcelAttachment,
    bool IncludeKpiSummary,
    bool IncludeReasonBreakdown,
    bool IncludeRecipientDomainBreakdown,
    bool IncludeDetailTable,
    bool IncludeSourceFileName,
    int MaxDetailRows,
    string BodyFormat,
    string? IntroText,
    string? FooterText)
{
    /// <summary>Bovengrens voor het aantal detailregels in de mail zelf.</summary>
    public const int MaxDetailRowsLimit = 1000;

    public const int DefaultMaxDetailRows = 100;

    public const string DefaultFooterText =
        "Dit overzicht wordt automatisch opgesteld. Wilt u het niet meer ontvangen, antwoord dan met " +
        "\"Afmelden\" in het onderwerp. Voor vragen over de inhoud kunt u gewoon op dit bericht reageren.";

    public static BounceNotificationContentOptions Default { get; } = new(
        IncludeExcelAttachment: true,
        IncludeKpiSummary: true,
        IncludeReasonBreakdown: true,
        IncludeRecipientDomainBreakdown: true,
        IncludeDetailTable: true,
        IncludeSourceFileName: true,
        MaxDetailRows: DefaultMaxDetailRows,
        BodyFormat: BounceNotificationBodyFormat.Default,
        IntroText: null,
        FooterText: DefaultFooterText);

    /// <summary>Alleen de kerncijfers, zonder bijlage en zonder detailregels.</summary>
    public static BounceNotificationContentOptions SummaryOnly { get; } = Default with
    {
        IncludeExcelAttachment = false,
        IncludeRecipientDomainBreakdown = false,
        IncludeDetailTable = false
    };

    public string ResolveBodyFormat() => BounceNotificationBodyFormat.Normalize(BodyFormat);

    public bool WantsHtml() => ResolveBodyFormat() != BounceNotificationBodyFormat.TextOnly;

    public bool WantsPlainText() => ResolveBodyFormat() != BounceNotificationBodyFormat.HtmlOnly;

    /// <summary>Aantal detailregels binnen de toegestane grenzen.</summary>
    public int ResolveMaxDetailRows()
    {
        if (MaxDetailRows <= 0)
        {
            return DefaultMaxDetailRows;
        }

        return Math.Min(MaxDetailRows, MaxDetailRowsLimit);
    }

    /// <summary>
    /// Zonder inhoudsblok blijft er niets te melden over. In dat geval wordt de samenvatting
    /// afgedwongen zodat er nooit een lege mail uitgaat.
    /// </summary>
    public BounceNotificationContentOptions EnsureNotEmpty()
    {
        bool hasContent = IncludeKpiSummary ||
                          IncludeReasonBreakdown ||
                          IncludeRecipientDomainBreakdown ||
                          IncludeDetailTable ||
                          !string.IsNullOrWhiteSpace(IntroText);

        return hasContent ? this : this with { IncludeKpiSummary = true };
    }
}
