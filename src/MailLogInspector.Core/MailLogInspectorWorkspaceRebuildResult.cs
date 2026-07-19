namespace MailLogInspector.Core;

public sealed record MailLogInspectorWorkspaceRebuildResult(
    bool WasRebuilt,
    string? BackupDatabasePath,
    int ImportedFileCount,
    int ImportedRowCount,
    string? SourceDatabasePath = null);
