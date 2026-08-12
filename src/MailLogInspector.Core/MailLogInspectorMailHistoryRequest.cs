namespace MailLogInspector.Core;

public sealed record MailLogInspectorMailHistoryRequest(
	string TrackingId,
	string Recipient,
	DateTime? FromInclusive = null,
	DateTime? ThroughInclusive = null);
