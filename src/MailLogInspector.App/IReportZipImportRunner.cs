namespace MailLogInspector.App;

public interface IReportZipImportRunner
{
    Task<GmailZipImportOutcome> ImportAsync(
        string zipPath,
        CancellationToken cancellationToken);
}
