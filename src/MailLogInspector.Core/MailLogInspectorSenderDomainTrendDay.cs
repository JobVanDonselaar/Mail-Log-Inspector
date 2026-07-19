namespace MailLogInspector.Core;

public sealed record MailLogInspectorSenderDomainTrendDay(
    DateTime Date,
    int TotalCount,
    int DeliveredCount,
    int UnderwayCount,
    int BounceCount,
    int DurationCount,
    int MissingDurationCount,
    double? AverageDurationSeconds,
    MailLogInspectorDurationBucket? P95DurationBucket);
