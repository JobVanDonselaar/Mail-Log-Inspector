namespace MailLogInspector.Core;

public enum MailLogInspectorReasonCode
{
	Delivered = 1,
	MailboxFull = 2,
	DnsProblem = 3,
	InvalidRecipient = 4,
	Timeout = 5,
	MessageExpired = 6,
	PolicyBlock = 7,
	Other = 8
}
