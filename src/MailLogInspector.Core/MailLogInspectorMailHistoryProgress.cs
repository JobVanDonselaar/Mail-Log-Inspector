namespace MailLogInspector.Core;

/// <summary>
/// Voortgang van het doorzoeken van de archieven, zodat de gebruiker ziet dat er iets gebeurt.
/// </summary>
public sealed record MailLogInspectorMailHistoryProgress(int Completed, int Total, string ArchiveName)
{
    public double Fraction => Total <= 0 ? 0d : (double)Completed / Total;

    public string Display => Total <= 0
        ? "Archief doorzoeken…"
        : $"Archief doorzoeken… {Completed} van {Total}";
}
