namespace MailLogInspector.Core;

public sealed record MailLogInspectorSenderDomainDashboard(
    string Domain,
    DateTime FromInclusive,
    DateTime ThroughInclusive,
    int TotalCount,
    int DeliveredCount,
    int UnderwayCount,
    int BounceCount,
    int DurationCount,
    int MissingDurationCount,
    double? AverageDurationSeconds,
    MailLogInspectorDurationBucket? P95DurationBucket,
    IReadOnlyList<MailLogInspectorSenderDomainTrendDay> Trend,
    IReadOnlyList<MailLogInspectorSenderDomainCause> TopCauses)
{
    public MailLogInspectorDurationDistribution DurationDistribution { get; init; } =
        MailLogInspectorDurationDistribution.Empty;

    public DateTime FromDate => FromInclusive.Date;

    public DateTime ThroughDate => ThroughInclusive.Date;

    public int DurationMissingCount => MissingDurationCount;
}
