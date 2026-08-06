using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MailLogInspector.App;

public sealed record SmtpApiStats(
    long Accepted,
    long Delivered,
    long Failed,
    long Queued,
    long Unsubscribed,
    long Complained)
{
    public double DeliveredPercent =>
        Accepted > 0 ? Math.Round(Delivered * 100.0 / Accepted, 1) : 0.0;
}

public interface ISmtpApiStatsService
{
    Task<SmtpApiStats> GetStatsAsync(
        string apiKey,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken);
}

public sealed class SmtpApiStatsService : ISmtpApiStatsService
{
    private const string ApiKeyHeaderName = "X-SMTPCOM-API";

    private readonly HttpClient _httpClient;

    public SmtpApiStatsService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<SmtpApiStats> GetStatsAsync(
        string apiKey,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("De SMTP.com API-sleutel ontbreekt.");
        }

        long startTs = start.ToUnixTimeSeconds();
        long endTs = end.ToUnixTimeSeconds();

        string url = $"{SmtpApiClient.BaseAddress}/stats?start={startTs}&end={endTs}&limit=1&offset=0";

        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation(ApiKeyHeaderName, apiKey.Trim());
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);

        string payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"SMTP.com stats mislukt ({(int)response.StatusCode}): {payload.Trim()}");
        }

        using JsonDocument doc = JsonDocument.Parse(payload);
        if (!doc.RootElement.TryGetProperty("data", out JsonElement data) ||
            !data.TryGetProperty("items", out JsonElement items) ||
            items.ValueKind != JsonValueKind.Array)
        {
            return new SmtpApiStats(0, 0, 0, 0, 0, 0);
        }

        foreach (JsonElement item in items.EnumerateArray())
        {
            return new SmtpApiStats(
                ReadLong(item, "accepted"),
                ReadLong(item, "delivered"),
                ReadLong(item, "failed"),
                ReadLong(item, "queued"),
                ReadLong(item, "unsubscribed"),
                ReadLong(item, "complained"));
        }

        return new SmtpApiStats(0, 0, 0, 0, 0, 0);
    }

    private static long ReadLong(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement v))
        {
            return 0;
        }

        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetInt64(out long n) ? n : 0,
            _ => 0
        };
    }
}
