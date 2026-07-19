namespace MailLogInspector.App;

public sealed record ReportImportedArtifact(
    string SourceHash,
    string FileName,
    DateTime? ReportDay,
    bool AlreadyImported);
