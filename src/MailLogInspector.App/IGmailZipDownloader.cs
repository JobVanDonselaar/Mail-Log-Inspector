namespace MailLogInspector.App;

public interface IGmailZipDownloader
{
    Task<string> DownloadAsync(string zipUrl, string targetDirectory, CancellationToken cancellationToken);
}
