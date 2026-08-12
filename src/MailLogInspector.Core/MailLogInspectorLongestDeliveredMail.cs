namespace MailLogInspector.Core;

public sealed record MailLogInspectorLongestDeliveredMail(
	DateTime AcceptedAt,
	DateTime DeliveredAt,
	string Sender,
	string Recipient,
	string TrackingId,
	int DurationSeconds,
	MailLogInspectorReasonCode ReasonCode,
	int? ResponseCode,
	string SourceFileName);
