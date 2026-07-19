using System;
using System.Collections.Generic;
using System.Globalization;

namespace MailLogInspector.Core;

public sealed record MailLogInspectorImportedFile(
    long ImportId,
    string SourcePath,
    string SourceFileName,
    string SourceHash,
    DateTime ImportedAt,
    DateTime? ReportStart,
    DateTime? ReportEnd,
    int RowCount,
    string? ArchivePath,
    int DeliveredCount = 0,
    int BounceCount = 0,
    int UnderwayCount = 0,
    IReadOnlyList<MailLogInspectorImportCause>? BounceCauses = null)
{
    public IReadOnlyList<MailLogInspectorImportCause> BounceCauses { get; init; } = BounceCauses ?? Array.Empty<MailLogInspectorImportCause>();

    public string DeliveredPercentDisplay => FormatPercent(DeliveredCount, RowCount);

    public string BouncePercentDisplay => FormatPercent(BounceCount, RowCount);

    public string UnderwayPercentDisplay => FormatPercent(UnderwayCount, RowCount);

    private static string FormatPercent(int count, int total)
    {
        if (total <= 0)
        {
            return "-";
        }

        return ((double)count * 100.0 / total).ToString("0.0", CultureInfo.GetCultureInfo("nl-NL")) + "%";
    }
}