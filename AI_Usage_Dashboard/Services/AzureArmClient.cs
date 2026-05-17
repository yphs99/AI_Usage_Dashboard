using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;

namespace AI_Usage_Dashboard.Services;

// Authenticated HTTP client for ARM endpoints — credential resolution + token refresh
// + paginated GET that returns raw JSON pages. No body transformation.
public sealed class AzureArmClient
{
    public const string ArmBaseUrl = "https://management.azure.com";
    private static readonly TokenRequestContext TokenContext = new(["https://management.azure.com/.default"]);

    private readonly TokenCredential _credential;
    private readonly HttpClient _http;

    public AzureArmClient(IConfiguration config)
    {
        _credential = BuildCredential(config);
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task EnsureAuthAsync(CancellationToken ct)
    {
        var token = await _credential.GetTokenAsync(TokenContext, ct);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
    }

    // GET → raw JsonDocument. Caller is responsible for disposing the JsonDocument when done.
    public async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        await EnsureAuthAsync(ct);
        using var response = await _http.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Azure ARM GET {response.StatusCode}: {body}");
        return JsonDocument.Parse(body);
    }

    // Iterates ARM list responses ({ value: [...], nextLink: "..." }) and yields each item element.
    // Caller can read fields lazily; document lifetime extends until enumeration completes (we keep
    // each page alive in a local list, returned as ReadOnlyMemory of cloned elements).
    public async IAsyncEnumerable<JsonElement> ListAllAsync(string firstUrl,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var url = firstUrl;
        while (!string.IsNullOrWhiteSpace(url))
        {
            using var doc = await GetJsonAsync(url, ct);
            if (doc.RootElement.TryGetProperty("value", out var valueEl) && valueEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in valueEl.EnumerateArray())
                    yield return item.Clone();
            }
            url = doc.RootElement.TryGetProperty("nextLink", out var nextEl)
                ? nextEl.GetString() ?? string.Empty
                : string.Empty;
        }
    }

    public async Task<JsonDocument> PostJsonWithRetryAsync(string url, object payload, CancellationToken ct)
    {
        await EnsureAuthAsync(ct);
        const int maxAttempts = 6;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var response = await _http.PostAsJsonAsync(url, payload, cancellationToken: ct);
            if ((int)response.StatusCode != 429 && (int)response.StatusCode != 503)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"Azure ARM POST {response.StatusCode}: {body}");
                return JsonDocument.Parse(body);
            }

            if (attempt == maxAttempts)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException($"Azure ARM POST exhausted retries — last status {response.StatusCode}, body: {body}");
            }

            var retryAfter = response.Headers.RetryAfter?.Delta
                ?? TimeSpan.FromSeconds(Math.Max(30, 10 * attempt));
            response.Dispose();
            await Task.Delay(retryAfter, ct);
        }

        throw new InvalidOperationException("Unexpected retry exit.");
    }

    private static TokenCredential BuildCredential(IConfiguration config)
    {
        var tenantId     = config["AzureCost:TenantId"];
        var clientId     = config["AzureCost:ClientId"];
        var clientSecret = config["AzureCost:ClientSecret"];
        if (!string.IsNullOrWhiteSpace(tenantId) &&
            !string.IsNullOrWhiteSpace(clientId) &&
            !string.IsNullOrWhiteSpace(clientSecret))
        {
            return new ClientSecretCredential(tenantId, clientId, clientSecret);
        }
        return new DefaultAzureCredential();
    }
}
