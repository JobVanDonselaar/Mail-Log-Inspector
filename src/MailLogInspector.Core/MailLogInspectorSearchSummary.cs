namespace MailLogInspector.Core;

public sealed record MailLogInspectorSearchSummary(int TotalCount, int DeliveredCount, int UnderwayCount, int BounceCount);
