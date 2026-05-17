using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using AI_Usage_Dashboard.Data;

namespace AI_Usage_Dashboard.Services;

// All OpenAI raw sync services: each fetches a single endpoint family and
// upserts the raw bucket / list elements straight into a *_raw collection.
// Architecture principle ①: no field mapping, no aggregation, no merging.

public sealed class OpenAiUsageRawSync(OpenAiHttpClient http, MongoDbContext db, ILogger<OpenAiUsageRawSync> logger)
{
    private const int BatchSize = 1000;

    // OpenAI's /organization/usage/{endpoint} accepts a different set of group_by
    // dimensions per endpoint. `model` for example is rejected on vector_stores /
    // code_interpreter_sessions (those endpoints have no model concept).
    // `batch` is a filter param, not a group_by — sending it always returns 400.
    private static readonly Dictionary<string, string[]> GroupBysByEndpoint = new()
    {
        ["completions"]               = ["project_id", "model", "user_id", "api_key_id"],
        ["responses"]                 = ["project_id", "model", "user_id", "api_key_id"],
        ["embeddings"]                = ["project_id", "model", "user_id", "api_key_id"],
        ["images"]                    = ["project_id", "model", "user_id", "api_key_id"],
        ["audio_speeches"]            = ["project_id", "model", "user_id", "api_key_id"],
        ["audio_transcriptions"]      = ["project_id", "model", "user_id", "api_key_id"],
        ["moderations"]               = ["project_id", "model", "user_id", "api_key_id"],
        // vector_stores / code_interpreter_sessions usage is tied to a project only
        // — no model, user, or api_key dimension. The API rejects any of those.
        ["vector_stores"]             = ["project_id"],
        ["code_interpreter_sessions"] = ["project_id"],
    };

    public async Task SyncAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        foreach (var (ms, me) in SplitByMonth(start, end))
        foreach (var (cs, ce) in SplitRange(ms, me, TimeSpan.FromDays(7)))
        foreach (var endpoint in GroupBysByEndpoint.Keys)
            await SyncEndpointAsync(endpoint, cs, ce, ct);
    }

    private async Task SyncEndpointAsync(string endpoint, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        string? page = null;
        var writes = new List<WriteModel<BsonDocument>>(BatchSize);
        var groupByQs = string.Join("&", GroupBysByEndpoint[endpoint].Select(g => $"group_by={g}"));

        while (true)
        {
            var url =
                $"organization/usage/{endpoint}" +
                $"?start_time={start.ToUnixTimeSeconds()}" +
                $"&end_time={end.ToUnixTimeSeconds()}" +
                "&bucket_width=1d&" + groupByQs +
                "&limit=31";
            if (!string.IsNullOrWhiteSpace(page))
                url += $"&page={Uri.EscapeDataString(page)}";

            JsonDocument doc;
            try { doc = await http.GetJsonAsync(url, ct); }
            catch (HttpRequestException ex) when (ex.Message.Contains("404")) { return; }

            string? nextPage = null;
            using (doc)
            {
                if (!doc.RootElement.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Array)
                    break;

                foreach (var bucket in dataEl.EnumerateArray())
                {
                    if (!bucket.TryGetProperty("results", out var resultsEl) || resultsEl.ValueKind != JsonValueKind.Array)
                        continue;
                    var startUnix = bucket.GetProperty("start_time").GetInt64();
                    var date = DateTime.SpecifyKind(DateTimeOffset.FromUnixTimeSeconds(startUnix).UtcDateTime.Date, DateTimeKind.Utc);

                    foreach (var r in resultsEl.EnumerateArray())
                    {
                        var raw = RawDoc.FromJson(r);
                        raw["endpoint"]  = endpoint;
                        raw["date"]      = date;
                        raw["projectId"] = raw.GetValue("project_id", BsonString.Empty)?.ToString() ?? string.Empty;
                        raw["model"]     = raw.GetValue("model",      BsonString.Empty)?.ToString() ?? string.Empty;
                        raw["userId"]    = raw.GetValue("user_id",    BsonString.Empty)?.ToString() ?? string.Empty;
                        raw["apiKeyId"]  = raw.GetValue("api_key_id", BsonString.Empty)?.ToString() ?? string.Empty;
                        raw["batch"]     = raw.GetValue("batch",      BsonNull.Value);

                        writes.Add(RawDoc.ReplaceByKey(raw,
                            ("endpoint",  raw["endpoint"]),
                            ("date",      raw["date"]),
                            ("projectId", raw["projectId"]),
                            ("model",     raw["model"]),
                            ("userId",    raw["userId"]),
                            ("apiKeyId",  raw["apiKeyId"]),
                            ("batch",     raw["batch"])));

                        if (writes.Count >= BatchSize)
                        {
                            await db.OpenAiUsageRaw.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false }, ct);
                            writes.Clear();
                        }
                    }
                }

                if (doc.RootElement.TryGetProperty("next_page", out var nextEl) && nextEl.ValueKind == JsonValueKind.String)
                    nextPage = nextEl.GetString();
            }

            if (string.IsNullOrWhiteSpace(nextPage)) break;
            page = nextPage;
        }

        if (writes.Count > 0)
            await db.OpenAiUsageRaw.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false }, ct);

        logger.LogDebug("OpenAI usage raw sync done for endpoint={Endpoint} [{Start}~{End}]", endpoint, start, end);
    }

    private static IEnumerable<(DateTimeOffset, DateTimeOffset)> SplitByMonth(DateTimeOffset start, DateTimeOffset end)
    {
        var cursor = new DateTimeOffset(start.Year, start.Month, 1, 0, 0, 0, TimeSpan.Zero);
        while (cursor < end)
        {
            var next = cursor.AddMonths(1);
            yield return (start > cursor ? start : cursor, end < next ? end : next);
            cursor = next;
        }
    }

    private static IEnumerable<(DateTimeOffset, DateTimeOffset)> SplitRange(DateTimeOffset start, DateTimeOffset end, TimeSpan chunk)
    {
        var cursor = start;
        while (cursor < end)
        {
            var next = cursor.Add(chunk);
            if (next > end) next = end;
            yield return (cursor, next);
            cursor = next;
        }
    }
}

public sealed class OpenAiCostsRawSync(OpenAiHttpClient http, MongoDbContext db, ILogger<OpenAiCostsRawSync> logger)
{
    private const int BatchSize = 1000;

    public async Task SyncAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        // OpenAI's /organization/costs only returns daily buckets whose
        // end_time ≤ the request's end_time — unlike /organization/usage, which
        // happily returns the partial bucket for the current day. To capture
        // today's still-accumulating bucket we ceil `end` up to the next UTC
        // midnight before chunking. (Future-dated end is harmless: the API
        // simply skips buckets that don't exist yet.)
        if (end.UtcDateTime != end.UtcDateTime.Date)
            end = new DateTimeOffset(end.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);

        foreach (var (ms, me) in SplitByMonth(start, end))
        foreach (var (cs, ce) in SplitRange(ms, me, TimeSpan.FromDays(7)))
            await SyncChunkAsync(cs, ce, ct);
    }

    private async Task SyncChunkAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var url =
            "organization/costs" +
            $"?start_time={start.ToUnixTimeSeconds()}" +
            $"&end_time={end.ToUnixTimeSeconds()}" +
            "&bucket_width=1d" +
            "&group_by=project_id&group_by=line_item" +
            "&limit=31";

        JsonDocument doc;
        try { doc = await http.GetJsonAsync(url, ct); }
        catch (HttpRequestException ex) { logger.LogWarning(ex, "openai costs failed {Url}", url); return; }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Array)
                return;

            var writes = new List<WriteModel<BsonDocument>>(BatchSize);
            foreach (var bucket in dataEl.EnumerateArray())
            {
                if (!bucket.TryGetProperty("results", out var resultsEl) || resultsEl.ValueKind != JsonValueKind.Array)
                    continue;
                var startUnix = bucket.GetProperty("start_time").GetInt64();
                var date = DateTime.SpecifyKind(DateTimeOffset.FromUnixTimeSeconds(startUnix).UtcDateTime.Date, DateTimeKind.Utc);

                foreach (var r in resultsEl.EnumerateArray())
                {
                    var raw = RawDoc.FromJson(r);
                    raw["date"]      = date;
                    raw["projectId"] = raw.GetValue("project_id", BsonString.Empty)?.ToString() ?? string.Empty;
                    raw["lineItem"]  = raw.GetValue("line_item",  BsonString.Empty)?.ToString() ?? string.Empty;

                    writes.Add(RawDoc.ReplaceByKey(raw,
                        ("date",      raw["date"]),
                        ("projectId", raw["projectId"]),
                        ("lineItem",  raw["lineItem"])));

                    if (writes.Count >= BatchSize)
                    {
                        await db.OpenAiCostsRaw.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false }, ct);
                        writes.Clear();
                    }
                }
            }
            if (writes.Count > 0)
                await db.OpenAiCostsRaw.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false }, ct);
        }
    }

    private static IEnumerable<(DateTimeOffset, DateTimeOffset)> SplitByMonth(DateTimeOffset start, DateTimeOffset end)
    {
        var cursor = new DateTimeOffset(start.Year, start.Month, 1, 0, 0, 0, TimeSpan.Zero);
        while (cursor < end)
        {
            var next = cursor.AddMonths(1);
            yield return (start > cursor ? start : cursor, end < next ? end : next);
            cursor = next;
        }
    }

    private static IEnumerable<(DateTimeOffset, DateTimeOffset)> SplitRange(DateTimeOffset start, DateTimeOffset end, TimeSpan chunk)
    {
        var cursor = start;
        while (cursor < end)
        {
            var next = cursor.Add(chunk);
            if (next > end) next = end;
            yield return (cursor, next);
            cursor = next;
        }
    }
}

// Combines orgs / users / api_keys raw sync.
//
// API key catalog gotchas (verified against the real API):
//   • Org-scope plain `/organization/api_keys` does NOT exist — returns 404.
//   • Real API keys are project-scoped: `/organization/projects/{pid}/api_keys`.
//   • Org-level admin keys live at `/organization/admin_api_keys` (a small set).
// We therefore enumerate every project from `openai_orgs_raw` and fan out, then
// also pull admin keys. Both go into `openai_api_keys_raw` with a `kind` tag so
// the read side can distinguish admin vs project keys when needed.
public sealed class OpenAiCatalogRawSync(OpenAiHttpClient http, MongoDbContext db, IConfiguration config)
{
    // When set, /organizations responses are filtered to this single id at upsert
    // time, and per-project api_keys only iterates this org's projects. This is
    // a hard org gate independent of the OpenAI-Organization header — we never
    // store data for other orgs even if the API returned them.
    private string? TargetOrgId => string.IsNullOrWhiteSpace(config["OpenAI:OrganizationId"])
        ? null
        : config["OpenAI:OrganizationId"]!.Trim();

    public async Task SyncAsync(CancellationToken ct)
    {
        // /organizations is account-level (returns every org the admin key sees).
        // We fetch it then filter at upsert.
        await SyncListAsync("organizations", db.OpenAiOrgsRaw,
            extra: null,
            shouldUpsert: id => TargetOrgId is null || string.Equals(id, TargetOrgId, StringComparison.Ordinal),
            ct);

        await SyncListAsync("organization/users", db.OpenAiUsersRaw, null, null, ct);
        await SyncApiKeysAsync(ct);
    }

    private async Task SyncApiKeysAsync(CancellationToken ct)
    {
        // 1. Org-level admin keys (single small list)
        await SyncListAsync("organization/admin_api_keys", db.OpenAiApiKeysRaw,
            extra: r => { r["kind"] = "admin"; }, shouldUpsert: null, ct);

        // 2. Per-project keys — read project ids out of openai_orgs_raw and fan out
        var orgs = await db.OpenAiOrgsRaw.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync(ct);
        foreach (var org in orgs)
        {
            if (!org.TryGetValue("projects", out var projEl)) continue;
            if (projEl.BsonType != BsonType.Document) continue;
            if (!projEl.AsBsonDocument.TryGetValue("data", out var dataEl) || dataEl.BsonType != BsonType.Array) continue;

            foreach (var p in dataEl.AsBsonArray)
            {
                if (p.BsonType != BsonType.Document) continue;
                var pid = p.AsBsonDocument.GetValue("id", BsonString.Empty).AsString;
                if (string.IsNullOrWhiteSpace(pid)) continue;

                var url = $"organization/projects/{Uri.EscapeDataString(pid)}/api_keys";
                await SyncListAsync(url, db.OpenAiApiKeysRaw,
                    extra: r => { r["kind"] = "project"; r["projectId"] = pid; },
                    shouldUpsert: null, ct);
            }
        }
    }

    public async Task<string> ResolvePreferredOrgIdAsync(string? configured, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();

        var f = Builders<BsonDocument>.Filter.Empty;
        var orgs = await db.OpenAiOrgsRaw.Find(f).ToListAsync(ct);
        var preferred = orgs.FirstOrDefault(o => o.GetValue("is_default", BsonBoolean.False).ToBoolean());
        var pick = preferred ?? orgs.FirstOrDefault();
        return pick is null ? string.Empty : pick.GetValue("_id", BsonString.Empty).AsString;
    }

    private async Task SyncListAsync(
        string baseUrl,
        IMongoCollection<BsonDocument> coll,
        Action<BsonDocument>? extra,
        Func<string, bool>? shouldUpsert,
        CancellationToken ct)
    {
        string? after = null;
        for (var i = 0; i < 200; i++)
        {
            var url = $"{baseUrl}?limit=100";
            if (!string.IsNullOrWhiteSpace(after))
                url += $"&after={Uri.EscapeDataString(after)}";

            JsonDocument doc;
            try { doc = await http.GetJsonAsync(url, ct); }
            catch (HttpRequestException) { break; }

            using (doc)
            {
                if (!doc.RootElement.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Array)
                    break;

                var writes = new List<WriteModel<BsonDocument>>();
                foreach (var item in dataEl.EnumerateArray())
                {
                    var id = RawDoc.ReadString(item, "id") ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    if (shouldUpsert is not null && !shouldUpsert(id)) continue;
                    var raw = RawDoc.FromJson(item);
                    raw["_id"] = id;
                    extra?.Invoke(raw);
                    writes.Add(new ReplaceOneModel<BsonDocument>(
                        Builders<BsonDocument>.Filter.Eq("_id", id), raw) { IsUpsert = true });
                }
                if (writes.Count > 0)
                    await coll.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false }, ct);

                if (!doc.RootElement.TryGetProperty("has_more", out var hasMoreEl) || !hasMoreEl.GetBoolean())
                    break;
                after = RawDoc.ReadString(doc.RootElement, "last_id");
                if (string.IsNullOrWhiteSpace(after)) break;
            }
        }
    }
}
