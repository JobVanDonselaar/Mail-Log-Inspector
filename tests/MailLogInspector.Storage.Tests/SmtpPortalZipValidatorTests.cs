using System.IO.Compression;
using MailLogInspector.App;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class SmtpPortalZipValidatorTests
{
    [Fact]
    public async Task ValidateAsync_ReturnsHashAndSizeForZipContainingCsv()
    {
        string path = CreateZip(("report.csv", "header,value\none,1"));

        SmtpPortalZipInspection inspection = await SmtpPortalZipValidator.ValidateAsync(path, CancellationToken.None);

        Assert.Equal(64, inspection.Sha256.Length);
        Assert.True(inspection.FileSize > 0);
        Assert.Equal("report.csv", inspection.CsvEntryName);
    }

    [Fact]
    public async Task ValidateAsync_RejectsZipWithoutCsv()
    {
        string path = CreateZip(("readme.txt", "not a report"));

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(
            () => SmtpPortalZipValidator.ValidateAsync(path, CancellationToken.None));

        Assert.Contains("CSV", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_RejectsCorruptZip()
    {
        string path = Path.Combine(Path.GetTempPath(), $"smtp-portal-corrupt-{Guid.NewGuid():N}.zip");
        await File.WriteAllTextAsync(path, "not a zip");

        await Assert.ThrowsAsync<InvalidDataException>(
            () => SmtpPortalZipValidator.ValidateAsync(path, CancellationToken.None));
    }

    [Fact]
    public async Task ValidateAsync_RejectsEmptyCsv()
    {
        string path = CreateZip(("report.csv", string.Empty));

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(
            () => SmtpPortalZipValidator.ValidateAsync(path, CancellationToken.None));

        Assert.Contains("leeg", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_RejectsMultipleCsvEntries()
    {
        string path = CreateZip(("one.csv", "a,b"), ("two.csv", "c,d"));

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(
            () => SmtpPortalZipValidator.ValidateAsync(path, CancellationToken.None));

        Assert.Contains("meerdere CSV", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateCsvEntry_RejectsUnsafeExpansionRatio()
    {
        string path = Path.Combine(Path.GetTempPath(), $"smtp-portal-ratio-{Guid.NewGuid():N}.zip");
        using (FileStream stream = File.Create(path))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            using StreamWriter writer = new(
                archive.CreateEntry("report.csv", CompressionLevel.SmallestSize).Open());
            writer.Write(new string('A', 100_000));
        }
        using ZipArchive readArchive = ZipFile.OpenRead(path);

        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => MailLogInspector.Core.MailLogInspectorImportLimits.ValidateCsvEntry(
                Assert.Single(readArchive.Entries)));

        Assert.Contains("compressieverhouding", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateZip(params (string Name, string Content)[] entries)
    {
        string path = Path.Combine(Path.GetTempPath(), $"smtp-portal-{Guid.NewGuid():N}.zip");
        using FileStream stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach ((string name, string content) in entries)
        {
            using StreamWriter writer = new(archive.CreateEntry(name).Open());
            writer.Write(content);
        }

        return path;
    }
}
