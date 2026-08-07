namespace MailLogInspector.Core;

/// <summary>Eén gebouncede mail binnen een import, gegroepeerd onder het afzenderadres.</summary>
public sealed record MailLogInspectorBounceRow(
    DateTime? AcceptedAt,
    string Recipient,
    MailLogInspectorReasonCode ReasonCode,
    int? ResponseCode,
    string LastMessage)
{
    public string ReasonDisplay => MailLogInspectorAttemptMeaning.DescribeBounceStatus(ReasonCode);

    public string ResponseDisplay => ResponseCode.HasValue
        ? ResponseCode.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : "-";

    public string AcceptedAtDisplay => AcceptedAt.HasValue
        ? AcceptedAt.Value.ToString("dd-MM-yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture)
        : "-";
}

/// <summary>Alle bounces van één afzenderadres binnen een import, inclusief afleverkerncijfers.</summary>
public sealed record MailLogInspectorSenderBounceReport(
    string SenderAddress,
    int TotalCount,
    int DeliveredCount,
    int UnderwayCount,
    int BounceCount,
    IReadOnlyList<MailLogInspectorBounceRow> Bounces)
{
    public string SenderDomain
    {
        get
        {
            int at = SenderAddress.LastIndexOf('@');
            return at >= 0 && at < SenderAddress.Length - 1 ? SenderAddress[(at + 1)..] : string.Empty;
        }
    }

    public double DeliveredPercent => TotalCount > 0
        ? Math.Round(DeliveredCount * 100.0 / TotalCount, 1)
        : 0.0;

    public double BouncePercent => TotalCount > 0
        ? Math.Round(BounceCount * 100.0 / TotalCount, 1)
        : 0.0;

    /// <summary>Bouncereden gesorteerd op aantal, aflopend.</summary>
    public IReadOnlyList<(string Reason, int Count)> ReasonBreakdown =>
        Bounces
            .GroupBy(row => row.ReasonDisplay, StringComparer.OrdinalIgnoreCase)
            .Select(group => (Reason: group.Key, Count: group.Count()))
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.Reason, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Ontvangende domeinen met de meeste bounces, aflopend.</summary>
    public IReadOnlyList<(string Domain, int Count)> RecipientDomainBreakdown =>
        Bounces
            .Select(row => ExtractDomain(row.Recipient))
            .Where(domain => domain.Length > 0)
            .GroupBy(domain => domain, StringComparer.OrdinalIgnoreCase)
            .Select(group => (Domain: group.Key, Count: group.Count()))
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.Domain, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string ExtractDomain(string address)
    {
        int at = address.LastIndexOf('@');
        return at >= 0 && at < address.Length - 1 ? address[(at + 1)..] : string.Empty;
    }
}
