namespace MailLogInspector.Storage;

public sealed record ReportImportSourceRow(
    string SourceHash,
    string Source,
    string FileName,
    DateTime? ReportDay,
    DateTime RecordedAtUtc);
