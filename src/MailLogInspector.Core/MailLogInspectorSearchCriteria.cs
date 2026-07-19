using System;

namespace MailLogInspector.Core;

public sealed record MailLogInspectorSearchCriteria(DateTime FromInclusive, DateTime ThroughInclusive, string? Sender, string? Recipient, string? SenderDomain, string? RecipientDomain, string? Status);
