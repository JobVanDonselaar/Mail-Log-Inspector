using System.IO;
using MailLogInspector.Core;
using MailLogInspector.Storage;

namespace MailLogInspector.App;

public static class ImportHistoryListBuilder
{
    public static IReadOnlyList<ImportHistoryListItem> Build(
        IReadOnlyList<MailLogInspectorImportedFile> imports,
        IReadOnlyList<GmailReportHistoryRow> gmailHistory,
        IReadOnlyList<ReportImportSourceRow>? recordedSources = null)
    {
        Dictionary<string, GmailReportHistoryRow> successfulGmailByHash = gmailHistory
            .Where(IsSuccessfulImportWithHash)
            .GroupBy(row => row.SourceHash!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(row => row.GmailInternalDate).First(),
                StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ReportImportSourceRow> sourceByHash = (recordedSources ?? [])
            .Where(row => !string.IsNullOrWhiteSpace(row.SourceHash))
            .GroupBy(row => row.SourceHash, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(row => row.RecordedAtUtc).First(),
                StringComparer.OrdinalIgnoreCase);
        HashSet<string> importHashes = imports
            .Select(import => import.SourceHash)
            .Where(hash => !string.IsNullOrWhiteSpace(hash))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<ImportHistoryListItem> rows = new(imports.Count + gmailHistory.Count);

        foreach (MailLogInspectorImportedFile import in imports)
        {
            successfulGmailByHash.TryGetValue(import.SourceHash, out GmailReportHistoryRow? gmail);
            sourceByHash.TryGetValue(import.SourceHash, out ReportImportSourceRow? recordedSource);
            rows.Add(CreateImportedRow(import, gmail, recordedSource?.Source));
        }

        foreach (GmailReportHistoryRow gmail in gmailHistory.Where(IsFailure))
        {
            if (!string.IsNullOrWhiteSpace(gmail.SourceHash) && importHashes.Contains(gmail.SourceHash))
            {
                continue;
            }

            rows.Add(CreateFailureRow(gmail));
        }

        return rows
            .OrderByDescending(row => row.Timestamp)
            .ThenBy(row => row.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsSuccessfulImportWithHash(GmailReportHistoryRow row)
    {
        return string.Equals(row.ImportStatus, "ok", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(row.SourceHash);
    }

    private static bool IsFailure(GmailReportHistoryRow row)
    {
        return string.Equals(row.DownloadStatus, "failed", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(row.ImportStatus, "failed", StringComparison.OrdinalIgnoreCase);
    }

    private static ImportHistoryListItem CreateImportedRow(
        MailLogInspectorImportedFile import,
        GmailReportHistoryRow? gmail,
        string? recordedSource)
    {
        string source = string.IsNullOrWhiteSpace(recordedSource)
            ? gmail is null ? ReportImportSource.Manual : ReportImportSource.Gmail
            : recordedSource;
        bool isImap = string.Equals(source, ReportImportSource.Gmail, StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(source, ReportImportSource.Imap, StringComparison.OrdinalIgnoreCase);
        string status = isImap && gmail is not null
            ? gmail.Archived
                ? "Gereed"
                : "Import gereed, verwijderen mislukt"
            : "Geïmporteerd";
        return new ImportHistoryListItem(
            isImap && gmail is not null ? gmail.GmailInternalDate.LocalDateTime : import.ImportedAt,
            source,
            import.SourceFileName,
            FormatReportPeriod(import.ReportStart, import.ReportEnd),
            import.RowCount,
            import.DeliveredCount,
            import.BounceCount,
            import.UnderwayCount,
            status,
            isImap ? gmail?.ErrorText : null);
    }

    private static ImportHistoryListItem CreateFailureRow(GmailReportHistoryRow gmail)
    {
        string status = string.Equals(gmail.DownloadStatus, "failed", StringComparison.OrdinalIgnoreCase)
            ? "Download mislukt"
            : "Import mislukt";
        return new ImportHistoryListItem(
            gmail.GmailInternalDate.LocalDateTime,
            ReportImportSource.Gmail,
            ReadFileName(gmail.ZipUrl),
            "-",
            null,
            null,
            null,
            null,
            status,
            gmail.ErrorText);
    }

    private static string FormatReportPeriod(DateTime? reportStart, DateTime? reportEnd)
    {
        if (!reportStart.HasValue && !reportEnd.HasValue)
        {
            return "-";
        }

        DateTime start = (reportStart ?? reportEnd)!.Value;
        DateTime end = (reportEnd ?? reportStart)!.Value;
        return start.Date == end.Date
            ? start.ToString("dd-MM-yyyy", MailLogInspectorDisplayFormats.Culture)
            : $"{start:dd-MM-yyyy} t/m {end:dd-MM-yyyy}";
    }

    private static string ReadFileName(string zipUrl)
    {
        if (Uri.TryCreate(zipUrl, UriKind.Absolute, out Uri? uri))
        {
            string fileName = Path.GetFileName(uri.AbsolutePath);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return fileName;
            }
        }

        return "-";
    }
}