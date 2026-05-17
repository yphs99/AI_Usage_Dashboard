using MongoDB.Bson;
using MongoDB.Driver;
using AI_Usage_Dashboard.Data;
using AI_Usage_Dashboard.Models;

namespace AI_Usage_Dashboard.Services;

// All read-side aggregation against the raw collections. Lives here so controllers
// stay thin and Aggregate pipelines can be unit-tested in isolation.
//
// Architecture principles applied:
//   ① raw collections are the source of truth — never reach into derived shapes
//   ④ aggregations run server-side via $match/$group/$lookup; C# only patches
//      the post-aggregation small result set with name lookup.
public sealed class UsageReadService(MongoDbContext db, NameLookupService nameLookup)
{
    // Endpoint → capability label translation as a Mongo $switch expression.
    // (Keep it data-driven via a $switch so the worker stays a pure sync.)
    private static readonly BsonDocument CapabilitySwitch = new("$switch", new BsonDocument
    {
        { "branches", new BsonArray
        {
            new BsonDocument { { "case", new BsonDocument("$eq", new BsonArray { "$endpoint", "completions" }) },               { "then", "Chat Completions" } },
            new BsonDocument { { "case", new BsonDocument("$eq", new BsonArray { "$endpoint", "responses" }) },                 { "then", "Responses"        } },
            new BsonDocument { { "case", new BsonDocument("$eq", new BsonArray { "$endpoint", "embeddings" }) },                { "then", "Embeddings"       } },
            new BsonDocument { { "case", new BsonDocument("$eq", new BsonArray { "$endpoint", "images" }) },                    { "then", "Images"           } },
            new BsonDocument { { "case", new BsonDocument("$eq", new BsonArray { "$endpoint", "audio_speeches" }) },            { "then", "Text to Speech"   } },
            new BsonDocument { { "case", new BsonDocument("$eq", new BsonArray { "$endpoint", "audio_transcriptions" }) },      { "then", "Transcription"    } },
            new BsonDocument { { "case", new BsonDocument("$eq", new BsonArray { "$endpoint", "moderations" }) },               { "then", "Moderation"       } },
            new BsonDocument { { "case", new BsonDocument("$eq", new BsonArray { "$endpoint", "vector_stores" }) },             { "then", "Vector Stores"    } },
            new BsonDocument { { "case", new BsonDocument("$eq", new BsonArray { "$endpoint", "code_interpreter_sessions" }) },{ "then", "Code Interpreter" } },
        } },
        { "default", "$endpoint" }
    });

    // ── OpenAI usage aggregate (raw → date-grain rows) ───────────────────────
    // Returns rows already grouped at (date, projectId, model, userId, apiKeyId)
    // with capability derived from endpoint via $switch.
    public async Task<List<UsageRecordDto>> AggregateOpenAiUsageAsync(
        string? orgId, string? projectId, string? model, string? capability,
        DateTime from, DateTime to, string? groupBy, CancellationToken ct)
    {
        var match = new BsonDocument("$match", BuildOpenAiMatch(orgId, projectId, model, capability, from, to));

        BsonValue groupId = (groupBy ?? string.Empty).ToLowerInvariant() switch
        {
            "project"     => "$projectId",
            "user"        => "$userId",
            "apikey"      => "$apiKeyId",
            "model"       => "$model",
            "servicetier" => new BsonString("default"),
            "date"        => new BsonDocument("$dateTrunc", new BsonDocument { { "date", "$date" }, { "unit", "day" } }),
            _             => new BsonDocument
            {
                { "date",      new BsonDocument("$dateTrunc", new BsonDocument { { "date", "$date" }, { "unit", "day" } }) },
                { "projectId", "$projectId" },
                { "model",     "$model" },
                { "userId",    "$userId" },
                { "apiKeyId",  "$apiKeyId" }
            }
        };

        var group = new BsonDocument("$group", new BsonDocument
        {
            { "_id",          groupId },
            { "date",         new BsonDocument("$max",   "$date") },
            { "projectId",    new BsonDocument("$first", "$projectId") },
            { "userId",       new BsonDocument("$first", "$userId") },
            { "apiKeyId",     new BsonDocument("$first", "$apiKeyId") },
            { "model",        new BsonDocument("$first", "$model") },
            // Sum core token / request counters from raw fields. Audio tokens are
            // NOT folded into input/output here — the read-side keeps them separate.
            { "inputTokens",  new BsonDocument("$sum", new BsonDocument("$ifNull", new BsonArray { "$input_tokens", 0 })) },
            { "outputTokens", new BsonDocument("$sum", new BsonDocument("$ifNull", new BsonArray { "$output_tokens", 0 })) },
            { "audioInput",   new BsonDocument("$sum", new BsonDocument("$ifNull", new BsonArray { "$input_audio_tokens", 0 })) },
            { "audioOutput",  new BsonDocument("$sum", new BsonDocument("$ifNull", new BsonArray { "$output_audio_tokens", 0 })) },
            { "requests",     new BsonDocument("$sum", new BsonDocument("$ifNull", new BsonArray { "$num_model_requests", 0 })) },
            { "capabilities", new BsonDocument("$addToSet", CapabilitySwitch) }
        });

        var project = new BsonDocument("$project", new BsonDocument
        {
            { "_id",          0 },
            { "date",         1 },
            { "projectId",    1 },
            { "userId",       1 },
            { "apiKeyId",     1 },
            { "model",        1 },
            { "inputTokens",  new BsonDocument("$add", new BsonArray { "$inputTokens",  "$audioInput"  }) },
            { "outputTokens", new BsonDocument("$add", new BsonArray { "$outputTokens", "$audioOutput" }) },
            { "requests",     1 },
            { "capability",   new BsonDocument("$cond", new BsonDocument
                {
                    { "if",   new BsonDocument("$eq", new BsonArray { new BsonDocument("$size", "$capabilities"), 1 }) },
                    { "then", new BsonDocument("$arrayElemAt", new BsonArray { "$capabilities", 0 }) },
                    { "else", "" }
                }) }
        });

        var pipeline = new[] { match, group, project };
        var rows = await db.OpenAiUsageRaw.Aggregate<BsonDocument>(pipeline, cancellationToken: ct).ToListAsync(ct);

        // C# enrich with names + costUsd from openai_costs_raw share-of-tokens
        var dtos = rows.Select(r => new UsageRecordDto
        {
            Date         = r.GetValue("date", BsonNull.Value).ToUniversalTime().ToString("o"),
            ProjectId    = r.GetValue("projectId", BsonString.Empty).AsString,
            UserId       = r.GetValue("userId",    BsonString.Empty).AsString,
            ApiKeyId     = r.GetValue("apiKeyId",  BsonString.Empty).AsString,
            Model        = r.GetValue("model",     BsonString.Empty).AsString,
            Capability   = r.GetValue("capability", BsonString.Empty).AsString,
            InputTokens  = r.GetValue("inputTokens",  BsonInt64.Create(0L)).ToInt64(),
            OutputTokens = r.GetValue("outputTokens", BsonInt64.Create(0L)).ToInt64(),
            Requests     = r.GetValue("requests",     BsonInt64.Create(0L)).ToInt64(),
            ServiceTier  = "default",
            Source       = "openai"
        }).ToList();

        await EnrichOpenAiNamesAsync(dtos, ct);
        await EnrichOpenAiCostsAsync(dtos, from, to, projectId, ct);
        if (!string.IsNullOrWhiteSpace(orgId))
            foreach (var d in dtos) d.OrgId = orgId!;

        return dtos;
    }

    // ── Azure usage aggregate (raw metrics → date-grain rows) ────────────────
    public async Task<List<UsageRecordDto>> AggregateAzureUsageAsync(
        string? subscriptionId, string? accountName, string? model, DateTime from, DateTime to,
        CancellationToken ct)
    {
        var matchFilter = new BsonDocument
        {
            { "dateUtc", new BsonDocument { { "$gte", from }, { "$lt", to } } }
        };
        if (!string.IsNullOrWhiteSpace(subscriptionId)) matchFilter["subscriptionId"] = subscriptionId;
        if (!string.IsNullOrWhiteSpace(accountName))    matchFilter["accountName"]    = accountName;
        if (!string.IsNullOrWhiteSpace(model))          matchFilter["modelName"]      = model;

        BsonDocument MetricSum(string metricSubstring) => new("$sum", new BsonDocument("$cond",
            new BsonDocument
            {
                { "if",   new BsonDocument("$regexMatch", new BsonDocument
                    {
                        { "input",   "$metricName" },
                        { "regex",   metricSubstring },
                        { "options", "i" }
                    }) },
                { "then", new BsonDocument("$ifNull", new BsonArray { "$total", 0 }) },
                { "else", 0 }
            }));

        var pipeline = new[]
        {
            new BsonDocument("$match", matchFilter),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument
                    {
                        { "date",           new BsonDocument("$dateTrunc", new BsonDocument { { "date", "$dateUtc" }, { "unit", "day" } }) },
                        { "subscriptionId", "$subscriptionId" },
                        { "accountName",    "$accountName" },
                        // modelName / deploymentName are stored as EMPTY STRINGS when the
                        // metric doesn't expose that dimension, so $ifNull alone won't
                        // fall through. Use $switch to fall back: modelName → deploymentName → "azure-openai".
                        { "model",          new BsonDocument("$switch", new BsonDocument
                            {
                                { "branches", new BsonArray
                                {
                                    new BsonDocument
                                    {
                                        { "case", new BsonDocument("$and", new BsonArray
                                            {
                                                new BsonDocument("$ne", new BsonArray { "$modelName", BsonNull.Value }),
                                                new BsonDocument("$ne", new BsonArray { "$modelName", "" })
                                            }) },
                                        { "then", "$modelName" }
                                    },
                                    new BsonDocument
                                    {
                                        { "case", new BsonDocument("$and", new BsonArray
                                            {
                                                new BsonDocument("$ne", new BsonArray { "$deploymentName", BsonNull.Value }),
                                                new BsonDocument("$ne", new BsonArray { "$deploymentName", "" })
                                            }) },
                                        { "then", "$deploymentName" }
                                    }
                                } },
                                { "default", "azure-openai" }
                            }) }
                    } },
                { "requests",     MetricSum("Requests|ModelRequests") },
                { "inputTokens",  MetricSum("InputTokens|PromptTokens|ProcessedPromptTokens|AudioInputTokens") },
                { "outputTokens", MetricSum("OutputTokens|CompletionTokens|GeneratedTokens|AudioOutputTokens") }
            }),
            // Azure Monitor emits availability/latency/error-rate metrics every interval
            // regardless of traffic, so a deployment with zero usage on a day still ends
            // up in $group with requests=tokens=0. Drop those rows here so usage detail
            // and cost breakdown don't display empty placeholders.
            new BsonDocument("$match", new BsonDocument("$or", new BsonArray
            {
                new BsonDocument("requests",     new BsonDocument("$gt", 0)),
                new BsonDocument("inputTokens",  new BsonDocument("$gt", 0)),
                new BsonDocument("outputTokens", new BsonDocument("$gt", 0)),
            })),
            new BsonDocument("$project", new BsonDocument
            {
                { "_id",          0 },
                { "date",         "$_id.date" },
                { "subscriptionId","$_id.subscriptionId" },
                { "accountName",  "$_id.accountName" },
                { "model",        "$_id.model" },
                { "requests",     1 },
                { "inputTokens",  1 },
                { "outputTokens", 1 }
            })
        };

        var rows = await db.AzureMetricsRaw.Aggregate<BsonDocument>(pipeline, cancellationToken: ct).ToListAsync(ct);

        // Azure cost lives in azure_cost_raw at (resourceId, meter, day) granularity, but
        // metric rows are at (account, deployment, day). Mirror what AzureBreakdownAsync
        // does: sum cost per (sub, account, day) then distribute across that day's rows
        // by token weight so the usage list shows a per-row cost approximation.
        var costMap = await BuildAzureCostMapAsync(subscriptionId, from, to, ct);

        // Pair each dto with its UTC date so we can group without re-parsing the ISO string.
        var pairs = rows.Select(r =>
        {
            var subId = r.GetValue("subscriptionId", BsonString.Empty).AsString;
            var acct  = r.GetValue("accountName",     BsonString.Empty).AsString;
            var date  = r.GetValue("date", BsonNull.Value).ToUniversalTime();
            var dto = new UsageRecordDto
            {
                Date         = date.ToString("o"),
                OrgId        = subId,
                ProjectId    = acct,
                ProjectName  = acct,
                Model        = r.GetValue("model",       BsonString.Empty).AsString,
                Capability   = "Azure OpenAI",
                InputTokens  = r.GetValue("inputTokens",  BsonInt64.Create(0L)).ToInt64(),
                OutputTokens = r.GetValue("outputTokens", BsonInt64.Create(0L)).ToInt64(),
                Requests     = r.GetValue("requests",     BsonInt64.Create(0L)).ToInt64(),
                ServiceTier  = "azure",
                Source       = "azure"
            };
            return (Dto: dto, DateInt: DateInt(date));
        }).ToList();

        // Distribute cost per (sub, account, dateInt) bucket by token weight.
        // azure_cost_raw stores accountName in lowercase via resourceId; lowercase
        // both sides so the lookup matches "OfficialProductionEastUS2" against the
        // stored "officialproductioneastus2".
        foreach (var grp in pairs.GroupBy(p =>
            (p.Dto.OrgId, p.Dto.ProjectId.ToLowerInvariant(), p.DateInt)))
        {
            if (!costMap.TryGetValue(grp.Key, out var groupCost) || groupCost <= 0) continue;
            var groupRows = grp.Select(g => g.Dto).ToArray();
            var weights = groupRows.Select(d => (decimal)(d.InputTokens + d.OutputTokens)).ToArray();
            var totalWeight = weights.Sum();
            if (totalWeight > 0)
                for (var i = 0; i < groupRows.Length; i++)
                    groupRows[i].CostUsd = groupCost * weights[i] / totalWeight;
            else
                groupRows[0].CostUsd = groupCost;
        }

        return pairs.Select(p => p.Dto).ToList();
    }

    private async Task<Dictionary<(string Sub, string Account, long DateInt), decimal>> BuildAzureCostMapAsync(
        string? subscriptionId, DateTime from, DateTime to, CancellationToken ct)
    {
        var match = new BsonDocument
        {
            { "usageDate", new BsonDocument
                {
                    { "$gte", long.Parse(from.ToString("yyyyMMdd")) },
                    { "$lt",  long.Parse(to.ToString("yyyyMMdd")) }
                } }
        };
        if (!string.IsNullOrWhiteSpace(subscriptionId)) match["subscriptionId"] = subscriptionId;

        var pipeline = new[]
        {
            new BsonDocument("$match", match),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument
                    {
                        { "subscriptionId", "$subscriptionId" },
                        { "resourceId",     "$resourceId" },
                        { "usageDate",      "$usageDate" }
                    } },
                { "cost", new BsonDocument("$sum", new BsonDocument("$toDecimal", new BsonDocument("$ifNull", new BsonArray { "$costUSD", 0 }))) }
            })
        };

        var costRows = await db.AzureCostRaw.Aggregate<BsonDocument>(pipeline, cancellationToken: ct).ToListAsync(ct);
        var map = new Dictionary<(string, string, long), decimal>();
        foreach (var c in costRows)
        {
            var key   = c["_id"].AsBsonDocument;
            var subId = key.GetValue("subscriptionId", BsonString.Empty).AsString;
            var resId = key.GetValue("resourceId",     BsonString.Empty).AsString;
            var date  = key.GetValue("usageDate",      BsonInt64.Create(0L)).ToInt64();
            var acct  = ExtractAccountFromResourceId(resId).ToLowerInvariant();
            if (string.IsNullOrEmpty(acct) || string.IsNullOrEmpty(subId) || date == 0) continue;
            var cost  = c.GetValue("cost", BsonDecimal128.Create(0m)).ToDecimal();
            var k = (subId, acct, date);
            map[k] = map.GetValueOrDefault(k) + cost;
        }
        return map;
    }

    // azure_cost_raw stores resourceId case-insensitively (sometimes "resourcegroups",
    // sometimes "resourceGroups"), and the segment after `/accounts/` is the account
    // name we group by elsewhere. metric rows already carry accountName in canonical
    // case, so emit the cost-side accountName in lowercase and look up case-insensitively.
    private static string ExtractAccountFromResourceId(string resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId)) return string.Empty;
        var parts = resourceId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length - 1; i++)
            if (parts[i].Equals("accounts", StringComparison.OrdinalIgnoreCase))
                return parts[i + 1];
        return string.Empty;
    }

    private static long DateInt(DateTime utcDate) =>
        long.Parse(utcDate.ToString("yyyyMMdd"));

    // ── Filter builders / enrichment ─────────────────────────────────────────

    private static BsonDocument BuildOpenAiMatch(string? orgId, string? projectId, string? model, string? capability,
                                                  DateTime from, DateTime to)
    {
        var match = new BsonDocument
        {
            { "date", new BsonDocument { { "$gte", from }, { "$lt", to } } }
        };
        if (!string.IsNullOrWhiteSpace(projectId))  match["projectId"] = projectId;
        if (!string.IsNullOrWhiteSpace(model))      match["model"]     = model;
        if (!string.IsNullOrWhiteSpace(capability)) match["endpoint"]  = CapabilityToEndpoint(capability);
        // orgId not applicable to openai_usage_raw (no orgId field on raw bucket result)
        return match;
    }

    private static string CapabilityToEndpoint(string capability) => capability switch
    {
        "Chat Completions" => "completions",
        "Responses"        => "responses",
        "Embeddings"       => "embeddings",
        "Images"           => "images",
        "Text to Speech"   => "audio_speeches",
        "Transcription"    => "audio_transcriptions",
        "Moderation"       => "moderations",
        "Vector Stores"    => "vector_stores",
        "Code Interpreter" => "code_interpreter_sessions",
        _                  => capability
    };

    private async Task EnrichOpenAiNamesAsync(IList<UsageRecordDto> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return;
        var projects = await nameLookup.GetProjectNamesAsync(ct);
        var users    = await nameLookup.GetUserNamesAsync(ct);
        var keys     = await nameLookup.GetApiKeyNamesAsync(ct);

        foreach (var r in rows)
        {
            if (!string.IsNullOrEmpty(r.ProjectId)) r.ProjectName = projects.GetValueOrDefault(r.ProjectId, r.ProjectId);
            if (!string.IsNullOrEmpty(r.UserId))    r.UserName    = users.GetValueOrDefault(r.UserId,    r.UserId);
            if (!string.IsNullOrEmpty(r.ApiKeyId))  r.ApiKeyName  = keys.GetValueOrDefault(r.ApiKeyId,   r.ApiKeyId);
        }
    }

    // Distribute project+date cost from openai_costs_raw across the rows that
    // share the same project+date by token-share (post-aggregation, small data set).
    private async Task EnrichOpenAiCostsAsync(IList<UsageRecordDto> rows, DateTime from, DateTime to,
                                              string? projectId, CancellationToken ct)
    {
        if (rows.Count == 0) return;

        var costFilter = new BsonDocument
        {
            { "date", new BsonDocument { { "$gte", from }, { "$lt", to } } }
        };
        if (!string.IsNullOrWhiteSpace(projectId)) costFilter["projectId"] = projectId;

        var pipeline = new[]
        {
            new BsonDocument("$match", costFilter),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument { { "projectId", "$projectId" }, { "date", "$date" } } },
                { "cost", new BsonDocument("$sum", new BsonDocument("$ifNull", new BsonArray
                    { new BsonDocument("$toDecimal", "$amount.value"), new BsonDocument("$toDecimal", 0) })) }
            })
        };
        var costRows = await db.OpenAiCostsRaw.Aggregate<BsonDocument>(pipeline, cancellationToken: ct).ToListAsync(ct);

        var costMap = costRows.ToDictionary(
            r =>
            {
                var key = r["_id"].AsBsonDocument;
                var pid = key.GetValue("projectId", BsonString.Empty).AsString;
                var dt  = key.GetValue("date", BsonNull.Value).ToUniversalTime().Date;
                return (pid, dt);
            },
            r => r.GetValue("cost", BsonDecimal128.Create(0m)).ToDecimal());

        foreach (var grp in rows.GroupBy(r => (r.ProjectId, Date: DateTime.Parse(r.Date).ToUniversalTime().Date)))
        {
            if (!costMap.TryGetValue(grp.Key, out var groupCost) || groupCost == 0) continue;
            var totalTokens = grp.Sum(x => x.InputTokens + x.OutputTokens);
            foreach (var r in grp)
            {
                var rTokens = r.InputTokens + r.OutputTokens;
                r.CostUsd = totalTokens > 0 ? groupCost * rTokens / totalTokens : 0m;
            }
        }
    }
}
