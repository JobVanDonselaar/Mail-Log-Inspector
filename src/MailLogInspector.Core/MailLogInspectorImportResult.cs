using System;

namespace MailLogInspector.Core;

public sealed record MailLogInspectorImportResult(bool AlreadyImported, long ImportId, string SourcePath, int SourceRowCount, int UpsertedCount, int ErrorCount, DateTime? ReportStart, DateTime? ReportEnd, string? ArchivePath, int ArchivedUpsertedCount = 0, int SkippedOldRows = 0);
