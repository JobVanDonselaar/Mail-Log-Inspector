namespace MailLogInspector.Core;

public sealed record MailLogInspectorDailyStatusTotals(
    DateTime Date,
    int Accepted,
    int Delivered,
    int Bounce,
    bool HasData);
