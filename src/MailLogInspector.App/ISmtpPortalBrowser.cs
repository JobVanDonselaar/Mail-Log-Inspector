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
        string windowTitle,
        CancellationToken cancellationToken);

    Task SetPageSizeAsync(int pageSize, CancellationToken cancellationToken);

    Task<IReadOnlyList<SmtpPortalReportRow>> ReadFirstPageReportsAsync(
        CancellationToken cancellationToken);

    Task<string> DownloadAsync(
        SmtpPortalReport report,
        string temporaryDirectory,
        CancellationToken cancellationToken);
}

public static class SmtpPortalBrowserTitles
{
    public const string Sync = "Mail Log Inspector - SMTP.com sync";
    public const string Diagnose = "Mail Log Inspector - SMTP.com diagnose";
}
