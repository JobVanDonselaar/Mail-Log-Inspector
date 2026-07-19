using System.IO.Compression;

namespace MailLogInspector.Core;

public static class MailLogInspectorImportLimits
{
    public const long MaxZipBytes = 512L * 1024 * 1024;
    public const long MaxCsvBytes = 3L * 1024 * 1024 * 1024;
    public const int MaxZipEntries = 100;
    public const double MaxCompressionRatio = 200.0;

    public static void ValidateFileSize(string path, long maximumBytes, string description)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException($"{description} ontbreekt.", path);
        }

        if (file.Length > maximumBytes)
        {
            throw new InvalidDataException(
                $"{description} is groter dan de toegestane limiet van {FormatMegabytes(maximumBytes)} MB.");
        }
    }

    public static void ValidateArchive(ZipArchive archive)
    {
        if (archive.Entries.Count > MaxZipEntries)
        {
            throw new InvalidDataException(
                $"Het ZIP-bestand bevat meer dan {MaxZipEntries} bestanden.");
        }
    }

    public static void ValidateCsvEntry(ZipArchiveEntry entry)
    {
        if (entry.Length == 0)
        {
            throw new InvalidDataException("Het CSV-bestand in het ZIP-bestand is leeg.");
        }

        if (entry.Length > MaxCsvBytes)
        {
            throw new InvalidDataException(
                $"Het uitgepakte CSV-bestand is groter dan de toegestane limiet van {FormatMegabytes(MaxCsvBytes)} MB.");
        }

        double compressionRatio = entry.Length / (double)Math.Max(1, entry.CompressedLength);
        if (compressionRatio > MaxCompressionRatio)
        {
            throw new InvalidDataException(
                "Het ZIP-bestand heeft een onveilige compressieverhouding.");
        }
    }

    private static long FormatMegabytes(long bytes) => bytes / (1024 * 1024);
}
