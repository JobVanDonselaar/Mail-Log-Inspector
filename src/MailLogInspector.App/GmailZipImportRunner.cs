using System.IO;
using System.Security.Cryptography;
using MailLogInspector.Storage;

namespace MailLogInspector.App;

public sealed class GmailZipImportRunner : IGmailZipImportRunner
{
    private readonly MailLogInspectorImportService _importService;

    public GmailZipImportRunner(MailLogInspectorImportService importService)
    {
        _importService = importService;
    }

    public async Task<GmailZipImportOutcome> ImportAsync(string zipPath, CancellationToken cancellationToken)
    {
        string sourceHash;
        using (FileStream stream = File.OpenRead(zipPath))
        {
            sourceHash = Convert.ToHexString(SHA256.HashData(stream));
        }

        var result = await _importService.ImportZipAsync(zipPath, cancellationToken);
        bool success =
            result.AlreadyImported ||
            result.SourceRowCount > 0 ||
            result.ErrorCount == 0;
        return new GmailZipImportOutcome(
            success,
            sourceHash,
            result.ReportStart,
            result.ReportEnd,
            result.AlreadyImported);
    }
}
