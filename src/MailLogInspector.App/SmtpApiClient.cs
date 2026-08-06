using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using MailLogInspector.Core;

namespace MailLogInspector.App;

public sealed record SmtpApiReport(
    string ReportId,
    string Name,
    string Status,
    string? Url,
    string? Channel,
    DateTime? RequestedAtUtc)
{
    public bool IsReady =>
        string.Equals(Status?.Trim(), "done", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(Url);
}

public sealed record SmtpApiChannel(string Name, string Status);

public interface ISmtpApiClient
{
    Task<IReadOnlyList<SmtpApiReport>> ListOndemandReportsAsync(
        string apiKey,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SmtpApiChannel>> ListChannelsAsync(
        string apiKey,
        CancellationToken cancellationToken);

    Task DownloadReportAsync(
        string url,
        string destinationPath,
        CancellationToken cancellationToken);
}

public sealed class SmtpApiClient : ISmtpApiClient
{
    public const string BaseAddress = "https://api.smtp.com/v4";
    private const string ApiKeyHeaderName = "X-SMTPCOM-API";

    private readonly HttpClient _httpClient;

    public SmtpApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<SmtpApiReport>> ListOndemandReportsAsync(
        string apiKey,
        CancellationToken cancellationToken)
    {
        using JsonDocument document = await GetJsonAsync("reports", apiKey, cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out JsonElement data) ||
            !data.TryGetProperty("ondemand", out JsonElement ondemand) ||
            ondemand.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<SmtpApiReport> reports = [];
        foreach (JsonElement item in ondemand.EnumerateArray())
        {
            string reportId = ReadString(item, "report_id") ?? string.Empty;
            string name = ReadString(item, "name") ?? string.Empty;
            if (reportId.Length == 0 || name.Length == 0)
            {
                continue;
            }

            reports.Add(new SmtpApiReport(
                reportId,
                name,
                ReadString(item, "status") ?? string.Empty,
                ReadString(item, "url"),
                ReadString(item, "channel"),
                ParseRequestedAt(ReadString(item, "time_req"))));
        }

        return reports;
    }

    public async Task<IReadOnlyList<SmtpApiChannel>> ListChannelsAsync(
        string apiKey,
        CancellationToken cancellationToken)
    {
        using JsonDocument document = await GetJsonAsync("channels", apiKey, cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out JsonElement data) ||
            !data.TryGetProperty("items", out JsonElement items) ||
            items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<SmtpApiChannel> channels = [];
        foreach (JsonElement item in items.EnumerateArray())
        {
            string name = ReadString(item, "name") ?? string.Empty;
            if (name.Length == 0)
            {
                continue;
            }

            channels.Add(new SmtpApiChannel(name, ReadString(item, "status") ?? string.Empty));
        }

        return channels;
    }

    public async Task DownloadReportAsync(
        string url,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException("Het rapport heeft geen downloadlocatie.");
        }

        using HttpRequestMessage request = new(HttpMethod.Get, url);
        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is long declaredLength &&
            declaredLength > MailLogInspectorImportLimits.MaxZipBytes)
        {
            throw new InvalidDataException(
                "Het aangeboden rapportbestand overschrijdt de maximale ZIP-grootte.");
        }

        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using FileStream target = new(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);
        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);

        byte[] buffer = new byte[81920];
        long written = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            written += read;
            if (written > MailLogInspectorImportLimits.MaxZipBytes)
            {
                throw new InvalidDataException(
                    "Het gedownloade rapportbestand overschrijdt de maximale ZIP-grootte.");
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (written == 0)
        {
            throw new InvalidDataException("Het gedownloade rapportbestand is leeg.");
        }
    }

    private async Task<JsonDocument> GetJsonAsync(
        string resource,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("De SMTP.com API-sleutel ontbreekt.");
        }

        // De API geeft 404 op paden met een afsluitende slash; gebruik daarom exact "{base}/{resource}".
        using HttpRequestMessage request = new(HttpMethod.Get, $"{BaseAddress}/{resource}");
        request.Headers.TryAddWithoutValidation(ApiKeyHeaderName, apiKey.Trim());
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);
        string payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(DescribeFailure(response.StatusCode, payload));
        }

        JsonDocument document = JsonDocument.Parse(payload);
        if (document.RootElement.TryGetProperty("status", out JsonElement status) &&
            !string.Equals(status.GetString(), "success", StringComparison.OrdinalIgnoreCase))
        {
            string message = ReadString(document.RootElement, "message") ?? "onbekende fout";
            document.Dispose();
            throw new InvalidOperationException($"SMTP.com API meldt een fout: {message}.");
        }

        return document;
    }

    private static string DescribeFailure(System.Net.HttpStatusCode statusCode, string payload)
    {
        string detail = statusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => "de API-sleutel is ongeldig of verlopen",
            System.Net.HttpStatusCode.Forbidden => "de API-sleutel heeft onvoldoende rechten",
            System.Net.HttpStatusCode.NotFound => "het opgevraagde API-pad bestaat niet",
            System.Net.HttpStatusCode.TooManyRequests => "de API-limiet is bereikt",
            _ => "de API gaf een onverwachte status"
        };
        string trimmed = payload.Length > 200 ? payload[..200] : payload;
        return $"SMTP.com API-aanroep mislukt ({(int)statusCode}): {detail}. {trimmed}".TrimEnd();
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static DateTime? ParseRequestedAt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out DateTimeOffset parsed)
            ? parsed.UtcDateTime
            : null;
    }
}
