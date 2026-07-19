namespace MailLogInspector.Core;

public sealed record MailLogInspectorSenderDomainCause(
    MailLogInspectorReasonCode ReasonCode,
    string Description,
    int Count);
