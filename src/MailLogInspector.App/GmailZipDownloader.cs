using System.IO;
using System.Net.Http;
using MailLogInspector.Core;

namespace MailLogInspector.App;

public sealed class GmailZipDownloader : IGmailZipDownloader
{
    private readonly HttpClient _httpClient;

    public GmailZipDownloader(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<string> DownloadAsync(string zipUrl, string targetDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(targetDirectory);
        string sourceFileName = Path.GetFileName(new Uri(zipUrl).AbsolutePath);
        string stem = Path.GetFileNameWithoutExtension(sourceFileName);
        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = "smtp-report";
        }

        string destinationPath = Path.Combine(
            targetDirectory,
            $"{stem}-{Guid.NewGuid():N}.zip");
        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(
                zipUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long contentLength &&
                contentLength > MailLogInspectorImportLimits.MaxZipBytes)
            {
                throw new InvalidDataException(
                    "Het IMAP-rapport is groter dan de toegestane downloadlimiet.");
            }

            await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using FileStream output = new(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);
            byte[] buffer = new byte[81920];
            long totalBytes = 0;
            while (true)
            {
                int bytesRead = await input.ReadAsync(buffer, cancellationToken);
                if (bytesRead == 0)
                {
                    break;
                }

                totalBytes += bytesRead;
                if (totalBytes > MailLogInspectorImportLimits.MaxZipBytes)
                {
                    throw new InvalidDataException(
                        "Het IMAP-rapport is groter dan de toegestane downloadlimiet.");
                }

                await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            }

            return destinationPath;
        }
        catch
        {
            File.Delete(destinationPath);
            throw;
        }
    }
}
