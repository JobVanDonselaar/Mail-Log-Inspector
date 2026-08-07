using System.Globalization;

namespace MailLogInspector.Storage;

/// <summary>Waarover een bouncemelding gaat: één import of een zelfgekozen periode.</summary>
public static class BounceNotificationScope
{
    /// <summary>De regels van één import.</summary>
    public const string Import = "import";

    /// <summary>Alle regels binnen een datumbereik, ongeacht welke import ze bracht.</summary>
    public const string Range = "range";

    public static string Normalize(string? value) =>
        string.Equals(value?.Trim(), Range, StringComparison.OrdinalIgnoreCase) ? Range : Import;
}

/// <summary>
/// De periode waarover een melding gaat. Hiermee is achteraf terug te zien welke dagen al
/// gemeld zijn, zodat een overgeslagen dag of week alsnog verstuurd kan worden.
/// </summary>
public sealed record BounceNotificationPeriod(
    string Scope,
    long? ImportId,
    DateTime FromInclusive,
    DateTime ThroughInclusive,
    string? SourceFileName)
{
    public static BounceNotificationPeriod ForImport(
        long importId,
        DateTime? reportStart,
        DateTime? reportEnd,
        string? sourceFileName)
    {
        DateTime end = (reportEnd ?? reportStart ?? DateTime.Today).Date;
        DateTime start = (reportStart ?? end).Date;
        return new BounceNotificationPeriod(
            BounceNotificationScope.Import,
            importId,
            start > end ? end : start,
            end,
            sourceFileName);
    }

    public static BounceNotificationPeriod ForRange(DateTime fromInclusive, DateTime throughInclusive)
    {
        DateTime start = fromInclusive.Date;
        DateTime end = throughInclusive.Date;
        if (end < start)
        {
            (start, end) = (end, start);
        }

        return new BounceNotificationPeriod(BounceNotificationScope.Range, null, start, end, null);
    }

    /// <summary>De datum die in onderwerp en bijlagenaam gebruikt wordt.</summary>
    public DateTime ReportDate => ThroughInclusive.Date;

    public string DescribePeriod()
    {
        return FromInclusive.Date == ThroughInclusive.Date
            ? FromInclusive.ToString("dd-MM-yyyy", CultureInfo.CurrentCulture)
            : $"{FromInclusive:dd-MM-yyyy} t/m {ThroughInclusive:dd-MM-yyyy}";
    }

    public string Describe()
    {
        return BounceNotificationScope.Normalize(Scope) == BounceNotificationScope.Range
            ? "Periode " + DescribePeriod()
            : $"Import {ImportId} ({DescribePeriod()})";
    }
}

/// <summary>Eén verstuurde of mislukte bouncemelding, zoals vastgelegd in het logboek.</summary>
public sealed record BounceNotificationLogEntry(
    long LogId,
    DateTime SentAtUtc,
    string SenderAddress,
    string Recipient,
    int BounceCount,
    string Scope,
    long? ImportId,
    DateTime? PeriodStart,
    DateTime? PeriodEnd,
    string? SourceFileName,
    bool Success,
    string? ErrorMessage)
{
    public DateTime SentAtLocal => SentAtUtc.Kind == DateTimeKind.Utc
        ? SentAtUtc.ToLocalTime()
        : SentAtUtc;

    public string PeriodDisplay
    {
        get
        {
            if (PeriodStart is null && PeriodEnd is null)
            {
                return ImportId is null ? "-" : $"Import {ImportId}";
            }

            DateTime start = (PeriodStart ?? PeriodEnd)!.Value;
            DateTime end = (PeriodEnd ?? PeriodStart)!.Value;
            return start.Date == end.Date
                ? start.ToString("dd-MM-yyyy", CultureInfo.CurrentCulture)
                : $"{start:dd-MM-yyyy} t/m {end:dd-MM-yyyy}";
        }
    }

    public string SentAtDisplay => SentAtLocal.ToString("dd-MM-yyyy HH:mm", CultureInfo.CurrentCulture);

    public string ResultDisplay => Success
        ? "Verstuurd"
        : "Mislukt: " + (string.IsNullOrWhiteSpace(ErrorMessage) ? "onbekende fout" : ErrorMessage);
}
