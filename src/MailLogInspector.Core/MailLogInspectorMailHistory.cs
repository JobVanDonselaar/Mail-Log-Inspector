using System;
using System.Collections.Generic;

namespace MailLogInspector.Core;

/// <summary>
/// Uitkomst van een archiefopzoeking voor één mail.
/// </summary>
public sealed record MailLogInspectorMailHistory(
    string TrackingId,
    string Recipient,
    IReadOnlyList<MailLogInspectorMailHistoryAttempt> Attempts,
    IReadOnlyList<string> SearchedArchives,
    IReadOnlyList<string> MissingArchives)
{
    public bool HasAttempts => Attempts.Count > 0;

    public static MailLogInspectorMailHistory Empty(string trackingId, string recipient) =>
        new(trackingId, recipient, Array.Empty<MailLogInspectorMailHistoryAttempt>(), Array.Empty<string>(), Array.Empty<string>());
}
