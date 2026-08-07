using System.Globalization;
using System.IO;
using MailLogInspector.Core;

namespace MailLogInspector.App;

/// <summary>
/// Schrijft de bounces van één afzender naar een Excel-bestand. Hergebruikt de bestaande
/// zoekresultaten-export zodat de opmaak gelijk is aan wat de gebruiker vanuit de app exporteert.
/// </summary>
public static class BounceNotificationExcelWriter
{
    public static string Write(
        string directory,
        MailLogInspectorSenderBounceReport report,
        DateTime reportDate)
    {
        Directory.CreateDirectory(directory);
        string fileName = BounceNotificationContentBuilder.BuildAttachmentFileName(report, reportDate);
        string path = Path.Combine(directory, fileName);

        SearchResultsExcelExporter.Export(path, ToSearchRows(report), domainDashboard: null);
        return path;
    }

    private static IReadOnlyList<MailLogInspectorSearchRow> ToSearchRows(
        MailLogInspectorSenderBounceReport report)
    {
        return report.Bounces
            .Select(row => new MailLogInspectorSearchRow(
                AcceptedAt: row.AcceptedAt,
                Sender: report.SenderAddress,
                Recipient: row.Recipient,
                TrackingId: string.Empty,
                Status: MailLogInspectorStatuses.Bounce,
                DurationSeconds: null,
                ReasonCode: row.ReasonCode,
                LastMessage: row.ResponseCode.HasValue
                    ? $"{row.ReasonDisplay} ({row.ResponseCode.Value.ToString(CultureInfo.InvariantCulture)})"
                    : row.ReasonDisplay,
                FirstSeenAt: row.AcceptedAt ?? DateTime.Now,
                LastSeenAt: row.AcceptedAt ?? DateTime.Now,
                SourceFileName: string.Empty))
            .ToList();
    }
}
