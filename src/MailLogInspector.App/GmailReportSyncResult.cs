namespace MailLogInspector.App;

public sealed record GmailReportSyncResult(
    int ImportedCount,
    int FailedCount,
    int SkippedCount,
    int DeletedCount,
    IReadOnlyList<ReportImportedArtifact>? ImportedArtifacts = null)
{
    public IReadOnlyList<ReportImportedArtifact> ImportedArtifacts { get; init; } =
        ImportedArtifacts ?? Array.Empty<ReportImportedArtifact>();
}