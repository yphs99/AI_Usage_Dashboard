using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using AI_Usage_Dashboard.Data;

namespace AI_Usage_Dashboard.Services;

// All Azure raw sync services. Each one fetches a single ARM endpoint family and
// upserts the response items directly. No parsing of resourceId, no derivation of
// accountName/resourceGroup (those happen at read time via $regexFind / $arrayElemAt).

// ── Subscriptions / Locations (cross-subscription metadata) ──────────────────
public sealed class AzureSubscriptionsRawSync(AzureArmClient arm, MongoDbContext db)
{
    public async Task SyncAsync(CancellationToken ct)
    {
        var writes = new List<WriteModel<BsonDocument>>();
        await foreach (var item in arm.ListAllAsync($"{AzureArmClient.ArmBaseUrl}/subscriptions?api-version=2020-01-01", ct))
        {
            var subId = RawDoc.ReadString(item, "subscriptionId");
            if (string.IsNullOrWhiteSpace(subId)) continue;
            var raw = RawDoc.FromJson(item);
            raw["_id"] = subId;
            writes.Add(new ReplaceOneModel<BsonDocument>(
                Builders<BsonDocument>.Filter.Eq("_id", subId), raw) { IsUpsert = true });
        }
        if (writes.Count > 0)
            await db.AzureSubscriptionsRaw.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false }, ct);
    }

    public async Task<List<(string SubscriptionId, string DisplayName)>> GetActiveAsync(CancellationToken ct)
    {
        var docs = await db.AzureSubscriptionsRaw.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync(ct);
        return docs
            .Where(d =>
            {
                var s = d.GetValue("state", "").AsString;
                return string.IsNullOrWhiteSpace(s)
                       || s.Equals("Enabled", StringComparison.OrdinalIgnoreCase)
                       || s.Equals("Warned",  StringComparison.OrdinalIgnoreCase);
            })
            .Select(d =>
            {
                var id = d.GetValue("subscriptionId", BsonString.Empty).AsString;
                var nm = d.GetValue("displayName",     BsonString.Empty).AsString;
                return (SubscriptionId: id, DisplayName: string.IsNullOrWhiteSpace(nm) ? id : nm);
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.SubscriptionId))
            .ToList();
    }
}

public sealed class AzureLocationsRawSync(AzureArmClient arm, MongoDbContext db)
{
    public async Task SyncAsync(string subscriptionId, CancellationToken ct)
    {
        var url = $"{AzureArmClient.ArmBaseUrl}/subscriptions/{Uri.EscapeDataString(subscriptionId)}/locations?api-version=2022-12-01";
        var writes = new List<WriteModel<BsonDocument>>();
        await foreach (var item in arm.ListAllAsync(url, ct))
        {
            var name = RawDoc.ReadString(item, "name");
            if (string.IsNullOrWhiteSpace(name)) continue;
            var raw = RawDoc.FromJson(item);
            raw["subscriptionId"] = subscriptionId;
            writes.Add(new ReplaceOneModel<BsonDocument>(
                Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Eq("subscriptionId", subscriptionId),
                    Builders<BsonDocument>.Filter.Eq("name", name)),
                raw) { IsUpsert = true });
        }
        if (writes.Count > 0)
            await db.AzureLocationsRaw.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false }, ct);
    }
}

// ── CognitiveServices accounts / deployments / usages ───────────────────────
public sealed class AzureAccountsRawSync(AzureArmClient arm, MongoDbContext db)
{
    public async Task<List<JsonElement>> SyncAsync(string subscriptionId, CancellationToken ct)
    {
        var url = $"{AzureArmClient.ArmBaseUrl}/subscriptions/{Uri.EscapeDataString(subscriptionId)}/providers/Microsoft.CognitiveServices/accounts?api-version=2024-10-01";
        var items = new List<JsonElement>();
        var writes = new List<WriteModel<BsonDocument>>();
        await foreach (var item in arm.ListAllAsync(url, ct))
        {
            var id = RawDoc.ReadString(item, "id");
            if (string.IsNullOrWhiteSpace(id)) continue;
            items.Add(item);
            var raw = RawDoc.FromJson(item);
            raw["_id"] = id;
            raw["subscriptionId"] = subscriptionId;
            writes.Add(new ReplaceOneModel<BsonDocument>(
                Builders<BsonDocument>.Filter.Eq("_id", id), raw) { IsUpsert = true });
        }
        if (writes.Count > 0)
            await db.AzureAccountsRaw.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false }, ct);
        return items;
    }
}

public sealed class AzureDeploymentsRawSync(AzureArmClient arm, MongoDbContext db)
{
    public async Task SyncAsync(string subscriptionId, string resourceGroup, string accountName, CancellationToken ct)
    {
        var url = $"{AzureArmClient.ArmBaseUrl}/subscriptions/{Uri.EscapeDataString(subscriptionId)}/resourceGroups/{Uri.EscapeDataString(resourceGroup)}/providers/Microsoft.CognitiveServices/accounts/{Uri.EscapeDataString(accountName)}/deployments?api-version=2024-10-01";
        var writes = new List<WriteModel<BsonDocument>>();
        await foreach (var item in arm.ListAllAsync(url, ct))
        {
            var id = RawDoc.ReadString(item, "id");
            if (string.IsNullOrWhiteSpace(id)) continue;
            var raw = RawDoc.FromJson(item);
            raw["_id"] = id;
            raw["subscriptionId"] = subscriptionId;
            raw["resourceGroup"]  = resourceGroup;
            raw["accountName"]    = accountName;
            writes.Add(new ReplaceOneModel<BsonDocument>(
                Builders<BsonDocument>.Filter.Eq("_id", id), raw) { IsUpsert = true });
        }
        if (writes.Count > 0)
            await db.AzureDeploymentsRaw.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false }, ct);
    }
}

public sealed class AzureUsagesRawSync(AzureArmClient arm, MongoDbContext db)
{
    public async Task SyncAsync(string subscriptionId, string resourceGroup, string accountName, string accountId, CancellationToken ct)
    {
        var url = $"{AzureArmClient.ArmBaseUrl}/subscriptions/{Uri.EscapeDataString(subscriptionId)}/resourceGroups/{Uri.EscapeDataString(resourceGroup)}/providers/Microsoft.CognitiveServices/accounts/{Uri.EscapeDataString(accountName)}/usages?api-version=2024-10-01";
        var writes = new List<WriteModel<BsonDocument>>();
        await foreach (var item in arm.ListAllAsync(url, ct))
        {
            var metricName = RawDoc.ReadString(item, "name", "value");
            if (string.IsNullOrWhiteSpace(metricName)) continue;
            var raw = RawDoc.FromJson(item);
            raw["accountId"]      = accountId;
            raw["subscriptionId"] = subscriptionId;
            raw["resourceGroup"]  = resourceGroup;
            raw["accountName"]    = accountName;
            raw["metricName"]     = metricName;
            writes.Add(new ReplaceOneModel<BsonDocument>(
                Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Eq("accountId", accountId),
                    Builders<BsonDocument>.Filter.Eq("metricName", metricName)),
                raw) { IsUpsert = true });
        }
        if (writes.Count > 0)
            await db.AzureUsagesRaw.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false }, ct);
    }
}

// ── Azure Monitor Metrics (metric defs + per-data-point upsert) ──────────────
public sealed class AzureMetricsRawSync(AzureArmClient arm, MongoDbContext db, ILogger<AzureMetricsRawSync> logger)
{
    public async Task SyncAccountAsync(
        string subscriptionId, string resourceGroup, string accountName, string accountId,
        DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken ct)
    {
        var defs = await SyncMetricDefinitionsAsync(accountId, ct);
        if (defs.Count == 0) return;

        // Azure Monitor enforces ≤20 metricnames per request (BadRequest otherwise).
        const int MaxMetricsPerCall = 20;

        // Group metrics by which model dimensions they expose so we can request
        // each group with the correct $filter. Without $filter every timeseries
        // collapses into a single dimensionless row → modelName / deploymentName
        // come back empty (the symptom that fed the "(unknown)" bar). Putting an
        // unsupported dimension in $filter returns 400 from Azure Monitor, hence
        // the per-group split.
        var groups = defs.Where(d => !string.IsNullOrWhiteSpace(d.Name))
                         .GroupBy(d => (d.SupportsModelName, d.SupportsModelDeploymentName));

        var writes = new List<WriteModel<BsonDocument>>();
        foreach (var grp in groups)
        {
            var (supMN, supMDN) = grp.Key;
            string? filter = (supMN, supMDN) switch
            {
                (true,  true)  => "ModelDeploymentName eq '*' and ModelName eq '*'",
                (false, true)  => "ModelDeploymentName eq '*'",
                (true,  false) => "ModelName eq '*'",
                _              => null,
            };

            var names = grp.Select(d => d.Name)
                           .Distinct(StringComparer.OrdinalIgnoreCase)
                           .ToList();

            for (var i = 0; i < names.Count; i += MaxMetricsPerCall)
            {
                var chunk = names.Skip(i).Take(MaxMetricsPerCall).ToList();
                var url =
                    $"{AzureArmClient.ArmBaseUrl}{accountId}/providers/microsoft.insights/metrics" +
                    "?api-version=2023-10-01" +
                    $"&timespan={Uri.EscapeDataString($"{windowStart.UtcDateTime:O}/{windowEnd.UtcDateTime:O}")}" +
                    "&interval=P1D&aggregation=Total" +
                    $"&metricnames={Uri.EscapeDataString(string.Join(",", chunk))}";
                if (filter is not null)
                    url += $"&$filter={Uri.EscapeDataString(filter)}";

                JsonDocument doc;
                try { doc = await arm.GetJsonAsync(url, ct); }
                catch (HttpRequestException ex)
                {
                    logger.LogWarning(ex, "metrics fetch failed for {Account} (group MN={MN},MDN={MDN}, chunk {Start}-{End})",
                        accountId, supMN, supMDN, i, i + chunk.Count - 1);
                    continue;
                }
                using (doc) AppendMetricWrites(doc.RootElement, accountId, subscriptionId, resourceGroup, accountName, writes);
            }
        }

        if (writes.Count > 0)
            await db.AzureMetricsRaw.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false }, ct);
    }

    private static void AppendMetricWrites(
        JsonElement root,
        string accountId, string subscriptionId, string resourceGroup, string accountName,
        List<WriteModel<BsonDocument>> writes)
    {
        if (!root.TryGetProperty("value", out var valueEl) || valueEl.ValueKind != JsonValueKind.Array)
            return;

        foreach (var metric in valueEl.EnumerateArray())
        {
            var metricName = RawDoc.ReadString(metric, "name", "value") ?? string.Empty;
            if (!metric.TryGetProperty("timeseries", out var seriesEl) || seriesEl.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var series in seriesEl.EnumerateArray())
            {
                var dims = new BsonDocument();
                if (series.TryGetProperty("metadatavalues", out var dimEl) && dimEl.ValueKind == JsonValueKind.Array)
                    foreach (var d in dimEl.EnumerateArray())
                    {
                        var dn = RawDoc.ReadString(d, "name", "value") ?? string.Empty;
                        var dv = RawDoc.ReadString(d, "value") ?? string.Empty;
                        if (!string.IsNullOrEmpty(dn)) dims[dn] = dv;
                    }

                // Azure Monitor's metadatavalues[].name.value is always lowercase
                // ("modeldeploymentname", not "ModelDeploymentName"). The PascalCase
                // form lives only in localizedValue. Look up by the lowercase key.
                // Azure also returns the literal "__Empty" placeholder when a metric
                // supports a dimension but has no value for that dimension on this
                // timeseries (e.g. Whisper deployment, TokenTransaction). Treat it
                // as empty so downstream fallback chains (modelName → deploymentName
                // → "azure-openai") work uniformly.
                static string NormDim(string s) => s == "__Empty" ? string.Empty : s;
                var deploymentName = NormDim(dims.GetValue("modeldeploymentname", BsonString.Empty)?.ToString() ?? string.Empty);
                if (string.IsNullOrEmpty(deploymentName))
                    deploymentName = NormDim(dims.GetValue("deployment", BsonString.Empty)?.ToString() ?? string.Empty);
                var modelName    = NormDim(dims.GetValue("modelname",    BsonString.Empty)?.ToString() ?? string.Empty);
                var modelVersion = NormDim(dims.GetValue("modelversion", BsonString.Empty)?.ToString() ?? string.Empty);
                var seriesRegion = NormDim(dims.GetValue("region",       BsonString.Empty)?.ToString() ?? string.Empty);

                if (!series.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var point in dataEl.EnumerateArray())
                {
                    if (!point.TryGetProperty("timeStamp", out var tsEl)) continue;
                    if (!DateTime.TryParse(tsEl.GetString(), null,
                            System.Globalization.DateTimeStyles.AdjustToUniversal, out var ts))
                        continue;

                    var doc2 = RawDoc.FromJson(point);
                    doc2["resourceId"]     = accountId;
                    doc2["subscriptionId"] = subscriptionId;
                    doc2["resourceGroup"]  = resourceGroup;
                    doc2["accountName"]    = accountName;
                    doc2["metricName"]     = metricName;
                    doc2["deploymentName"] = deploymentName;
                    doc2["modelName"]      = modelName;
                    doc2["modelVersion"]   = modelVersion;
                    doc2["region"]         = seriesRegion;
                    doc2["dateUtc"]        = DateTime.SpecifyKind(ts.Date, DateTimeKind.Utc);
                    doc2["dimensions"]     = dims;

                    writes.Add(new ReplaceOneModel<BsonDocument>(
                        Builders<BsonDocument>.Filter.And(
                            Builders<BsonDocument>.Filter.Eq("resourceId",     accountId),
                            Builders<BsonDocument>.Filter.Eq("metricName",     metricName),
                            Builders<BsonDocument>.Filter.Eq("deploymentName", deploymentName),
                            Builders<BsonDocument>.Filter.Eq("modelName",      modelName),
                            Builders<BsonDocument>.Filter.Eq("modelVersion",   modelVersion),
                            Builders<BsonDocument>.Filter.Eq("region",         seriesRegion),
                            Builders<BsonDocument>.Filter.Eq("dateUtc",        doc2["dateUtc"])),
                        doc2) { IsUpsert = true });
                }
            }
        }
    }

    private async Task<List<MetricDef>> SyncMetricDefinitionsAsync(string resourceId, CancellationToken ct)
    {
        var url = $"{AzureArmClient.ArmBaseUrl}{resourceId}/providers/microsoft.insights/metricDefinitions?api-version=2023-10-01";
        var defs = new List<MetricDef>();
        var writes = new List<WriteModel<BsonDocument>>();
        await foreach (var item in arm.ListAllAsync(url, ct))
        {
            var name = RawDoc.ReadString(item, "name", "value");
            if (string.IsNullOrWhiteSpace(name)) continue;
            var unit = RawDoc.ReadString(item, "unit") ?? string.Empty;

            var supMN  = false;
            var supMDN = false;
            if (item.TryGetProperty("dimensions", out var dimsEl) && dimsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in dimsEl.EnumerateArray())
                {
                    var v = RawDoc.ReadString(d, "value") ?? string.Empty;
                    if (v == "ModelName")           supMN  = true;
                    if (v == "ModelDeploymentName") supMDN = true;
                }
            }
            defs.Add(new MetricDef(name, unit, supMN, supMDN));

            var raw = RawDoc.FromJson(item);
            raw["resourceId"] = resourceId;
            raw["metricName"] = name;
            writes.Add(new ReplaceOneModel<BsonDocument>(
                Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Eq("resourceId", resourceId),
                    Builders<BsonDocument>.Filter.Eq("metricName", name)),
                raw) { IsUpsert = true });
        }
        if (writes.Count > 0)
            await db.AzureMetricDefsRaw.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false }, ct);
        return defs;
    }

    private readonly record struct MetricDef(string Name, string Unit, bool SupportsModelName, bool SupportsModelDeploymentName);
}

// ── Azure Cost Management Query (raw column/row matrix) ──────────────────────
public sealed class AzureCostRawSync(AzureArmClient arm, MongoDbContext db, ILogger<AzureCostRawSync> logger)
{
    public async Task SyncAsync(string subscriptionId, string subscriptionName,
        DateTime fromUtc, DateTime toUtcExclusive, CancellationToken ct)
    {
        var url = $"{AzureArmClient.ArmBaseUrl}/subscriptions/{Uri.EscapeDataString(subscriptionId)}/providers/Microsoft.CostManagement/query?api-version=2023-03-01";
        var payload = new
        {
            type      = "Usage",
            timeframe = "Custom",
            timePeriod = new
            {
                from = fromUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                to   = toUtcExclusive.ToString("yyyy-MM-ddTHH:mm:ssZ")
            },
            dataset = new
            {
                granularity = "Daily",
                aggregation = new Dictionary<string, object>
                {
                    ["totalCost"] = new { name = "CostUSD", function = "Sum" }
                },
                grouping = new object[]
                {
                    new { type = "Dimension", name = "ResourceId" },
                    new { type = "Dimension", name = "ResourceGroup" },
                    new { type = "Dimension", name = "ResourceLocation" },
                    new { type = "Dimension", name = "MeterCategory" },
                    new { type = "Dimension", name = "MeterSubCategory" },
                    new { type = "Dimension", name = "Meter" },
                    new { type = "Dimension", name = "ServiceName" }
                }
            }
        };

        JsonDocument doc;
        try { doc = await arm.PostJsonWithRetryAsync(url, payload, ct); }
        catch (HttpRequestException ex) { logger.LogWarning(ex, "azure cost query failed for {Sub}", subscriptionId); return; }

        var writes = new List<WriteModel<BsonDocument>>();
        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("properties", out var props)) return;
            if (!props.TryGetProperty("columns", out var columnsEl) || columnsEl.ValueKind != JsonValueKind.Array) return;
            if (!props.TryGetProperty("rows",    out var rowsEl)    || rowsEl.ValueKind    != JsonValueKind.Array) return;

            var columns = columnsEl.EnumerateArray()
                .Select((c, i) => (Name: RawDoc.ReadString(c, "name") ?? string.Empty, Index: i))
                .ToList();

            foreach (var row in rowsEl.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array) continue;
                var values = row.EnumerateArray().ToList();

                var raw = new BsonDocument();
                foreach (var (name, idx) in columns)
                {
                    if (idx >= values.Count) continue;
                    raw[NormalizeColumnName(name)] = JsonElementToBson(values[idx]);
                }

                raw["subscriptionId"]   = subscriptionId;
                raw["subscriptionName"] = subscriptionName;
                raw["updatedAtUtc"]     = DateTime.UtcNow;
                // The `currency` column the API returns is the subscription's billing
                // currency (e.g. "TWD"); it does NOT describe the unit of `costUSD`.
                // We pin a separate marker so consumers don't have to know that.
                raw["costCurrency"]     = "USD";

                // Filter must match every column the unique index spans; otherwise
                // duplicate-key errors fire when the API returns multiple rows that
                // share the (sub, resourceId, meter, day) tuple but differ on
                // resourceLocation / meterCategory / meterSubCategory / serviceName
                // (Cost Management groups by all 7 dimensions).
                BsonValue Field(string n) => raw.GetValue(n, BsonString.Empty) ?? BsonString.Empty;

                writes.Add(new ReplaceOneModel<BsonDocument>(
                    Builders<BsonDocument>.Filter.And(
                        Builders<BsonDocument>.Filter.Eq("subscriptionId",   subscriptionId),
                        Builders<BsonDocument>.Filter.Eq("resourceId",       Field("resourceId")),
                        Builders<BsonDocument>.Filter.Eq("resourceGroup",    Field("resourceGroup")),
                        Builders<BsonDocument>.Filter.Eq("resourceLocation", Field("resourceLocation")),
                        Builders<BsonDocument>.Filter.Eq("meterCategory",    Field("meterCategory")),
                        Builders<BsonDocument>.Filter.Eq("meterSubCategory", Field("meterSubCategory")),
                        Builders<BsonDocument>.Filter.Eq("meter",            Field("meter")),
                        Builders<BsonDocument>.Filter.Eq("serviceName",      Field("serviceName")),
                        Builders<BsonDocument>.Filter.Eq("usageDate",        Field("usageDate"))),
                    raw) { IsUpsert = true });
            }
        }
        if (writes.Count > 0)
            await db.AzureCostRaw.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false }, ct);
    }

    private static string NormalizeColumnName(string raw) =>
        string.IsNullOrEmpty(raw) ? raw : char.ToLowerInvariant(raw[0]) + raw[1..];

    private static BsonValue JsonElementToBson(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? BsonNull.Value as BsonValue,
        JsonValueKind.Number => v.TryGetInt64(out var l) ? new BsonInt64(l) : (BsonValue)new BsonDouble(v.GetDouble()),
        JsonValueKind.True   => BsonBoolean.True,
        JsonValueKind.False  => BsonBoolean.False,
        JsonValueKind.Null or JsonValueKind.Undefined => BsonNull.Value,
        _ => v.ToString()
    };
}

// ── Top-level Azure full snapshot orchestrator ───────────────────────────────
// Calls all the smaller raw sync services in sequence. NO derivation, NO
// region accumulator, NO synthetic collections — those are read-time concerns.
public sealed class AzureSnapshotOrchestrator(
    AzureSubscriptionsRawSync subsSvc,
    AzureLocationsRawSync     locsSvc,
    AzureAccountsRawSync      acctSvc,
    AzureDeploymentsRawSync   deploySvc,
    AzureUsagesRawSync        usageSvc,
    AzureMetricsRawSync       metricsSvc,
    AzureCostRawSync          costSvc,
    ILogger<AzureSnapshotOrchestrator> logger)
{
    public async Task SyncAsync(DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken ct)
    {
        await subsSvc.SyncAsync(ct);
        var subscriptions = await subsSvc.GetActiveAsync(ct);

        foreach (var sub in subscriptions)
        {
            try
            {
                await locsSvc.SyncAsync(sub.SubscriptionId, ct);
                var accounts = await acctSvc.SyncAsync(sub.SubscriptionId, ct);

                foreach (var account in accounts)
                {
                    var accountId   = RawDoc.ReadString(account, "id") ?? string.Empty;
                    var accountName = RawDoc.ReadString(account, "name") ?? string.Empty;
                    var rg          = ExtractResourceGroup(accountId);
                    if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(accountName) || string.IsNullOrWhiteSpace(rg))
                        continue;

                    await deploySvc.SyncAsync(sub.SubscriptionId, rg, accountName, ct);
                    await usageSvc.SyncAsync(sub.SubscriptionId, rg, accountName, accountId, ct);
                    await metricsSvc.SyncAccountAsync(sub.SubscriptionId, rg, accountName, accountId, windowStart, windowEnd, ct);
                }

                await costSvc.SyncAsync(sub.SubscriptionId, sub.DisplayName, windowStart.UtcDateTime, windowEnd.UtcDateTime, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Azure snapshot sync failed for subscription {Sub}", sub.SubscriptionId);
            }
        }
    }

    // The only piece of "parsing" we keep here is identifying which resourceGroup
    // a deployment fetch needs. resourceGroup itself stays raw inside the documents
    // (read side uses it as-is); we only need it as a URL parameter.
    private static string ExtractResourceGroup(string resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId)) return string.Empty;
        var parts = resourceId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length - 1; i++)
            if (parts[i].Equals("resourceGroups", StringComparison.OrdinalIgnoreCase))
                return parts[i + 1];
        return string.Empty;
    }
}
