using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using MailLogInspector.Core;

namespace MailLogInspector.App;

public sealed record SmtpPortalZipInspection(
    string Sha256,
    long FileSize,
    string CsvEntryName);

public static class SmtpPortalZipValidator
{
    public static async Task<SmtpPortalZipInspection> ValidateAsync(string path, CancellationToken cancellationToken)
    {
        FileInfo file = new(path);
        if (!file.Exists || file.Length < 4)
        {
            throw new InvalidDataException("Het gedownloade ZIP-bestand ontbreekt of is leeg.");
        }
        MailLogInspectorImportLimits.ValidateFileSize(
            path,
            MailLogInspectorImportLimits.MaxZipBytes,
            "Het gedownloade ZIP-bestand");

        string sha256;
        await using (FileStream hashStream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            byte[] hash = await SHA256.HashDataAsync(hashStream, cancellationToken);
            sha256 = Convert.ToHexString(hash);
        }

        try
        {
            using ZipArchive archive = ZipFile.OpenRead(path);
            MailLogInspectorImportLimits.ValidateArchive(archive);
            List<ZipArchiveEntry> csvEntries = archive.Entries
                .Where(entry =>
                    !string.IsNullOrWhiteSpace(entry.Name) &&
                    string.Equals(Path.GetExtension(entry.Name), ".csv", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (csvEntries.Count == 0)
            {
                throw new InvalidDataException("Het gedownloade ZIP-bestand bevat geen CSV-bestand.");
            }
            if (csvEntries.Count > 1)
            {
                throw new InvalidDataException(
                    "Het gedownloade ZIP-bestand bevat meerdere CSV-bestanden.");
            }

            ZipArchiveEntry csvEntry = csvEntries[0];
            MailLogInspectorImportLimits.ValidateCsvEntry(csvEntry);
            await using Stream csvStream = csvEntry.Open();
            byte[] probe = new byte[1];
            int bytesRead = await csvStream.ReadAsync(probe, cancellationToken);
            if (bytesRead == 0)
            {
                throw new InvalidDataException(
                    "Het CSV-bestand in het gedownloade ZIP-bestand is leeg.");
            }

            return new SmtpPortalZipInspection(sha256, file.Length, csvEntry.Name);
        }
        catch (InvalidDataException ex) when (!ex.Message.Contains("CSV", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Het gedownloade bestand is geen geldig ZIP-bestand.", ex);
        }
    }
}
