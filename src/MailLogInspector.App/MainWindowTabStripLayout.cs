namespace MailLogInspector.App;

/// <summary>
/// Berekent waar het statuspaneel rechtsboven mag beginnen. Het paneel ligt over de tabstrip
/// heen, dus een vaste marge dekt tabbladen af zodra er een tab bijkomt. Door de werkelijke
/// tabbreedte te meten blijft elk tabblad aanklikbaar.
/// </summary>
public static class MainWindowTabStripLayout
{
    /// <summary>Ruimte tussen de laatste tab en het statuspaneel.</summary>
    public const double TabStripGap = 10;

    /// <summary>Onder deze breedte is het statuspaneel niet meer leesbaar en blijft het verborgen.</summary>
    public const double MinimumStatusWidth = 240;

    /// <summary>
    /// De linkermarge voor het statuspaneel, gemeten vanaf de linkerrand van het venster.
    /// Geeft null wanneer er te weinig ruimte overblijft; het paneel hoort dan verborgen te zijn.
    /// </summary>
    public static double? CalculateStatusLeftMargin(
        double tabStripWidth,
        double windowWidth,
        double tabControlLeftMargin,
        double statusRightMargin)
    {
        if (tabStripWidth <= 0 || windowWidth <= 0)
        {
            return null;
        }

        double left = tabControlLeftMargin + tabStripWidth + TabStripGap;
        double available = windowWidth - left - statusRightMargin;

        return available < MinimumStatusWidth ? null : left;
    }
}
