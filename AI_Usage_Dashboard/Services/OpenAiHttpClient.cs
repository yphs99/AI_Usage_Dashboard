using System.Text.Json;

namespace AI_Usage_Dashboard.Services;

// Thin wrapper over HttpClient with retry on retryable status codes.
// Returns raw JsonDocument so callers can upsert the original payload directly
// (architecture principle ①: no transformation in the worker / sync services).
public sealed class OpenAiHttpClient(HttpClient http)
{
    public async Task<JsonDocument> GetJsonAsync(string relativeUrl, CancellationToken ct = default)
    {
        const int maxRetry = 5;
        var delay = TimeSpan.FromSeconds(2);

        for (var attempt = 1; attempt <= maxRetry; attempt++)
        {
            using var response = await http.GetAsync(relativeUrl, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
                return JsonDocument.Parse(body);

            var code = (int)response.StatusCode;
            var retryable = code is 408 or 429 or 500 or 502 or 503 or 504;

            if (!retryable || attempt == maxRetry)
                throw new HttpRequestException($"HTTP {code} from '{relativeUrl}'. Body: {body}");

            await Task.Delay(delay, ct);
            delay = TimeSpan.FromSeconds(delay.TotalSeconds * 2);
        }

        throw new InvalidOperationException("Unexpected retry exit.");
    }
}
