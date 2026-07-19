namespace MailLogInspector.Core;

public sealed record MailLogInspectorDeliveryLatencyDay(
    DateTime Date,
    int DeliveredCount,
    int DurationCount,
    long DurationSumSeconds,
    int MissingDurationCount,
    int Within60Count,
    int Within300Count,
    int Within900Count,
    int Within3600Count);
