namespace MailLogInspector.App;

public sealed record SmtpPortalCredentials(
    string Username,
    string Password,
    IReadOnlyList<string> TotpCodes);

public interface ISmtpPortalBrowser : IAsyncDisposable
{
    Task InitializeAsync(
        SmtpPortalCredentials credentials,
        bool visible,
        CancellationToken cancellationToken);

    Task SetPageSizeAsync(int pageSize, CancellationToken cancellationToken);

    Task<IReadOnlyList<SmtpPortalReportRow>> ReadFirstPageReportsAsync(
        CancellationToken cancellationToken);

    Task<string> DownloadAsync(
        SmtpPortalReport report,
        string temporaryDirectory,
        CancellationToken cancellationToken);
}
