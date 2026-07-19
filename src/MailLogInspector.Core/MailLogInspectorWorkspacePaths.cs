namespace MailLogInspector.Core;

public sealed record MailLogInspectorWorkspacePaths(
    string RootDirectory,
    string DatabasePath,
    string ArchiveDirectory,
    string IncomingDirectory,
    string ArchiveDatabaseDirectory,
    string GmailOperationalDatabasePath,
    string GmailIncomingDirectory);
