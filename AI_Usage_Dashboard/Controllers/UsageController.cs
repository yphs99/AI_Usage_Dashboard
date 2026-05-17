using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using AI_Usage_Dashboard.Data;
using AI_Usage_Dashboard.Models;
using AI_Usage_Dashboard.Services;
using AI_Usage_Dashboard.Utils;

namespace AI_Usage_Dashboard.Controllers;

[ApiController]
[Route("v1/usage")]
public sealed class UsageController(MongoDbContext db, UsageReadService usageReadService) : ControllerBase
{
    [HttpGet("overview")]
    public async Task<OverviewStats> GetOverview(
        [FromQuery] string? orgId,
        [FromQuery] string? projectId,
        [FromQuery] string? period   = "MTD",
        [FromQuery] string? startDate = null,
        [FromQuery] string? endDate   = null,
        [FromQuery] string? source    = "all")
    {
        var src = NormalizeSource(source);
        var (from, to)         = DateRangeHelper.ResolvePeriod(period, startDate, endDate);
        var (prevFrom, prevTo) = DateRangeHelper.PreviousPeriod(from, to);

        var current  = await AggregateAsync(src, orgId, projectId, from, to);
        var previous = await AggregateAsync(src, orgId, projectId, prevFrom, prevTo);

        var days = Math.Max(1, (to - from).Days);

        return new OverviewStats
        {
            MonthlyCost        = current.Cost,
            MonthlyCostDelta   = MetricsHelper.Delta(current.Cost,     previous.Cost),
            TotalRequests      = current.Requests,
            TotalRequestsDelta = MetricsHelper.Delta(current.Requests, previous.Requests),
            InputTokens        = current.InputTokens,
            OutputTokens       = current.OutputTokens,
            AvgDailyCost       = current.Cost / days,
            ErrorRate          = 0
        };
    }

    [HttpGet("trend")]
    public async Task<List<DailyTrendPoint>> GetTrend(
        [FromQuery] string? orgId,
        [FromQuery] string? projectId,
        [FromQuery] string? period   = "30d",
        [FromQuery] string? startDate = null,
        [FromQuery] string? endDate   = null,
        [FromQuery] string? source    = "all")
    {
        var src = NormalizeSource(source);
        var (from, to) = DateRangeHelper.ResolvePeriod(period, startDate, endDate);
        var ct = HttpContext.RequestAborted;

        var merged = new Dictionary<DateTime, DailyTrendPoint>();

        if (src is "all" or "openai")
        {
            // Aggregate by date directly on raw, then merge with cost from openai_costs_raw
            var usagePipeline = new[]
            {
                new BsonDocument("$match", BuildOpenAiUsageMatch(projectId, from, to)),
                new BsonDocument("$group", new BsonDocument
                {
                    { "_id", new BsonDocument("$dateTrunc", new BsonDocument { { "date", "$date" }, { "unit", "day" } }) },
                    { "requests",     new BsonDocument("$sum", new BsonDocument("$ifNull", new BsonArray { "$num_model_requests", 0 })) },
                    { "inputTokens",  new BsonDocument("$sum", new BsonDocument("$add", new BsonArray
                        {
                            new BsonDocument("$ifNull", new BsonArray { "$input_tokens",       0 }),
                            new BsonDocument("$ifNull", new BsonArray { "$input_audio_tokens", 0 })
                        })) },
                    { "outputTokens", new BsonDocument("$sum", new BsonDocument("$add", new BsonArray
                        {
                            new BsonDocument("$ifNull", new BsonArray { "$output_tokens",       0 }),
                            new BsonDocument("$ifNull", new BsonArray { "$output_audio_tokens", 0 })
                        })) }
                })
            };
            var usageRows = await db.OpenAiUsageRaw.Aggregate<BsonDocument>(usagePipeline, cancellationToken: ct).ToListAsync(ct);
            foreach (var r in usageRows)
            {
                var d = r["_id"].ToUniversalTime().Date;
                merged[d] = new DailyTrendPoint
                {
                    Date         = d.ToString("yyyy-MM-dd"),
                    Requests     = r.GetValue("requests",     BsonInt64.Create(0L)).ToInt64(),
                    InputTokens  = r.GetValue("inputTokens",  BsonInt64.Create(0L)).ToInt64(),
                    OutputTokens = r.GetValue("outputTokens", BsonInt64.Create(0L)).ToInt64()
                };
            }

            var costPipeline = new[]
            {
                new BsonDocument("$match", BuildOpenAiCostMatch(projectId, from, to)),
                new BsonDocument("$group", new BsonDocument
                {
                    { "_id", "$date" },
                    { "cost", new BsonDocument("$sum", new BsonDocument("$toDecimal", new BsonDocument("$ifNull", new BsonArray { "$amount.value", 0 }))) }
                })
            };
            var costRows = await db.OpenAiCostsRaw.Aggregate<BsonDocument>(costPipeline, cancellationToken: ct).ToListAsync(ct);
            foreach (var r in costRows)
            {
                var d = r["_id"].ToUniversalTime().Date;
                var cost = r.GetValue("cost", BsonDecimal128.Create(0m)).ToDecimal();
                if (!merged.TryGetValue(d, out var p))
                    merged[d] = new DailyTrendPoint { Date = d.ToString("yyyy-MM-dd"), Cost = cost };
                else
                    p.Cost = cost;
            }
        }

        if (src is "all" or "azure")
        {
            var rows = await usageReadService.AggregateAzureUsageAsync(orgId, null, null, from, to, ct);
            foreach (var grp in rows.GroupBy(x => DateTime.Parse(x.Date).ToUniversalTime().Date))
            {
                if (!merged.TryGetValue(grp.Key, out var p))
                    merged[grp.Key] = p = new DailyTrendPoint { Date = grp.Key.ToString("yyyy-MM-dd") };
                p.Requests     += grp.Sum(x => x.Requests);
                p.InputTokens  += grp.Sum(x => x.InputTokens);
                p.OutputTokens += grp.Sum(x => x.OutputTokens);
            }

            var azCostPipeline = new[]
            {
                new BsonDocument("$match", BuildAzureCostMatch(orgId, from, to)),
                new BsonDocument("$group", new BsonDocument
                {
                    // usageDate is Int64 yyyyMMdd; cast to string then parse as date.
                    { "_id", new BsonDocument("$dateFromString", new BsonDocument
                        {
                            { "dateString", new BsonDocument("$toString", "$usageDate") },
                            { "format",     "%Y%m%d" },
                            { "onError",    BsonNull.Value }
                        }) },
                    { "cost", new BsonDocument("$sum", new BsonDocument("$toDecimal", new BsonDocument("$ifNull", new BsonArray { "$costUSD", 0 }))) }
                })
            };
            var azCostRows = await db.AzureCostRaw.Aggregate<BsonDocument>(azCostPipeline, cancellationToken: ct).ToListAsync(ct);
            foreach (var r in azCostRows)
            {
                var dv = r["_id"];
                if (dv.IsBsonNull) continue;
                var d = dv.ToUniversalTime().Date;
                var cost = r.GetValue("cost", BsonDecimal128.Create(0m)).ToDecimal();
                if (!merged.TryGetValue(d, out var p))
                    merged[d] = new DailyTrendPoint { Date = d.ToString("yyyy-MM-dd"), Cost = cost };
                else
                    p.Cost += cost;
            }
        }

        return merged.Values.OrderBy(x => x.Date).ToList();
    }

    [HttpGet("records")]
    public async Task<PaginatedResponse<UsageRecordDto>> GetRecords([FromQuery] UsageQueryParams q)
    {
        var (from, to) = DateRangeHelper.ResolvePeriod(q.Period, q.StartDate, q.EndDate);
        var src = NormalizeSource(q.Source);
        var ct  = HttpContext.RequestAborted;

        var rows = new List<UsageRecordDto>();
        if (src is "all" or "openai")
            rows.AddRange(await usageReadService.AggregateOpenAiUsageAsync(
                q.OrgId, q.ProjectId, q.Model, q.Capability, from, to, q.GroupBy, ct));
        if (src is "all" or "azure")
            rows.AddRange(await usageReadService.AggregateAzureUsageAsync(
                q.OrgId, q.ProjectId, q.Model, from, to, ct));

        rows = SortRows(rows, q.SortBy, q.SortDir).ToList();

        var total    = rows.LongCount();
        var pageRows = rows.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToList();

        return new PaginatedResponse<UsageRecordDto>
        {
            Data       = pageRows,
            Total      = total,
            Page       = q.Page,
            PageSize   = q.PageSize,
            TotalPages = (int)Math.Ceiling((double)total / q.PageSize)
        };
    }

    [HttpGet("filters")]
    public async Task<IActionResult> GetFilters([FromQuery] string? source = "all")
    {
        var src = NormalizeSource(source);
        var ct  = HttpContext.RequestAborted;

        var models       = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var capabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (src is "all" or "openai")
        {
            // Distinct from raw (collection-driven, principle ②)
            var distinctModels = await db.OpenAiUsageRaw.Distinct<string>("model", Builders<BsonDocument>.Filter.Empty).ToListAsync(ct);
            foreach (var m in distinctModels.Where(s => !string.IsNullOrWhiteSpace(s))) models.Add(m);

            var distinctEndpoints = await db.OpenAiUsageRaw.Distinct<string>("endpoint", Builders<BsonDocument>.Filter.Empty).ToListAsync(ct);
            foreach (var e in distinctEndpoints.Where(s => !string.IsNullOrWhiteSpace(s)))
                capabilities.Add(EndpointToCapability(e));
        }

        if (src is "all" or "azure")
        {
            var azureModels = await db.AzureDeploymentsRaw.Distinct<string>("properties.model.name", Builders<BsonDocument>.Filter.Empty).ToListAsync(ct);
            foreach (var m in azureModels.Where(s => !string.IsNullOrWhiteSpace(s))) models.Add(m);
            capabilities.Add("Azure OpenAI");
        }

        return Ok(new
        {
            models       = models.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            capabilities = capabilities.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<(decimal Cost, long Requests, long InputTokens, long OutputTokens)> AggregateAsync(
        string source, string? orgId, string? projectId, DateTime from, DateTime to)
    {
        var ct = HttpContext.RequestAborted;
        var totals = (Cost: 0m, Requests: 0L, InputTokens: 0L, OutputTokens: 0L);

        if (source is "all" or "openai")
        {
            // tokens / requests
            var p1 = new[]
            {
                new BsonDocument("$match", BuildOpenAiUsageMatch(projectId, from, to)),
                new BsonDocument("$group", new BsonDocument
                {
                    { "_id", 1 },
                    { "requests",     new BsonDocument("$sum", new BsonDocument("$ifNull", new BsonArray { "$num_model_requests", 0 })) },
                    { "inputTokens",  new BsonDocument("$sum", new BsonDocument("$add", new BsonArray
                        {
                            new BsonDocument("$ifNull", new BsonArray { "$input_tokens",       0 }),
                            new BsonDocument("$ifNull", new BsonArray { "$input_audio_tokens", 0 })
                        })) },
                    { "outputTokens", new BsonDocument("$sum", new BsonDocument("$add", new BsonArray
                        {
                            new BsonDocument("$ifNull", new BsonArray { "$output_tokens",       0 }),
                            new BsonDocument("$ifNull", new BsonArray { "$output_audio_tokens", 0 })
                        })) }
                })
            };
            var u = (await db.OpenAiUsageRaw.Aggregate<BsonDocument>(p1, cancellationToken: ct).ToListAsync(ct)).FirstOrDefault();
            if (u is not null)
            {
                totals.Requests     += u.GetValue("requests",     BsonInt64.Create(0L)).ToInt64();
                totals.InputTokens  += u.GetValue("inputTokens",  BsonInt64.Create(0L)).ToInt64();
                totals.OutputTokens += u.GetValue("outputTokens", BsonInt64.Create(0L)).ToInt64();
            }

            // costs
            var p2 = new[]
            {
                new BsonDocument("$match", BuildOpenAiCostMatch(projectId, from, to)),
                new BsonDocument("$group", new BsonDocument
                {
                    { "_id", 1 },
                    { "cost", new BsonDocument("$sum", new BsonDocument("$toDecimal", new BsonDocument("$ifNull", new BsonArray { "$amount.value", 0 }))) }
                })
            };
            var c = (await db.OpenAiCostsRaw.Aggregate<BsonDocument>(p2, cancellationToken: ct).ToListAsync(ct)).FirstOrDefault();
            if (c is not null)
                totals.Cost += c.GetValue("cost", BsonDecimal128.Create(0m)).ToDecimal();
        }

        if (source is "all" or "azure")
        {
            var azRows = await usageReadService.AggregateAzureUsageAsync(orgId, projectId, null, from, to, ct);
            totals.Requests     += azRows.Sum(x => x.Requests);
            totals.InputTokens  += azRows.Sum(x => x.InputTokens);
            totals.OutputTokens += azRows.Sum(x => x.OutputTokens);

            var p3 = new[]
            {
                new BsonDocument("$match", BuildAzureCostMatch(orgId, from, to)),
                new BsonDocument("$group", new BsonDocument
                {
                    { "_id", 1 },
                    { "cost", new BsonDocument("$sum", new BsonDocument("$toDecimal", new BsonDocument("$ifNull", new BsonArray { "$costUSD", 0 }))) }
                })
            };
            var ac = (await db.AzureCostRaw.Aggregate<BsonDocument>(p3, cancellationToken: ct).ToListAsync(ct)).FirstOrDefault();
            if (ac is not null)
                totals.Cost += ac.GetValue("cost", BsonDecimal128.Create(0m)).ToDecimal();
        }

        return totals;
    }

    private static BsonDocument BuildOpenAiUsageMatch(string? projectId, DateTime from, DateTime to)
    {
        var f = new BsonDocument { { "date", new BsonDocument { { "$gte", from }, { "$lt", to } } } };
        if (!string.IsNullOrWhiteSpace(projectId)) f["projectId"] = projectId;
        return f;
    }

    private static BsonDocument BuildOpenAiCostMatch(string? projectId, DateTime from, DateTime to)
    {
        var f = new BsonDocument { { "date", new BsonDocument { { "$gte", from }, { "$lt", to } } } };
        if (!string.IsNullOrWhiteSpace(projectId)) f["projectId"] = projectId;
        return f;
    }

    private static BsonDocument BuildAzureCostMatch(string? subscriptionId, DateTime from, DateTime to)
    {
        // ARM Cost Management Query returns `usageDate` as a numeric yyyyMMdd
        // (verified against real responses — stored as BsonInt64 in azure_cost_raw).
        // Mongo does not coerce string ↔ number in $match, so we MUST compare as
        // numbers; otherwise every range query returns 0 documents.
        var f = new BsonDocument();
        if (!string.IsNullOrWhiteSpace(subscriptionId))
            f["subscriptionId"] = subscriptionId;

        f["usageDate"] = new BsonDocument
        {
            { "$gte", long.Parse(from.ToString("yyyyMMdd")) },
            { "$lt",  long.Parse(to.ToString("yyyyMMdd")) }
        };
        return f;
    }

    private static IEnumerable<UsageRecordDto> SortRows(IEnumerable<UsageRecordDto> rows, string? sortBy, string sortDir)
    {
        var asc = string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
        return (sortBy ?? "").ToLowerInvariant() switch
        {
            "project"      => asc ? rows.OrderBy(x => x.ProjectName)  : rows.OrderByDescending(x => x.ProjectName),
            "user"         => asc ? rows.OrderBy(x => x.UserName)     : rows.OrderByDescending(x => x.UserName),
            "model"        => asc ? rows.OrderBy(x => x.Model)        : rows.OrderByDescending(x => x.Model),
            "capability"   => asc ? rows.OrderBy(x => x.Capability)   : rows.OrderByDescending(x => x.Capability),
            "inputtokens"  => asc ? rows.OrderBy(x => x.InputTokens)  : rows.OrderByDescending(x => x.InputTokens),
            "outputtokens" => asc ? rows.OrderBy(x => x.OutputTokens) : rows.OrderByDescending(x => x.OutputTokens),
            "requests"     => asc ? rows.OrderBy(x => x.Requests)     : rows.OrderByDescending(x => x.Requests),
            "costusd"      => asc ? rows.OrderBy(x => x.CostUsd)      : rows.OrderByDescending(x => x.CostUsd),
            _              => asc ? rows.OrderBy(x => x.Date)         : rows.OrderByDescending(x => x.Date)
        };
    }

    private static string NormalizeSource(string? source)
    {
        var v = (source ?? "all").Trim().ToLowerInvariant();
        return v is "openai" or "azure" ? v : "all";
    }

    private static string EndpointToCapability(string endpoint) => endpoint switch
    {
        "completions"               => "Chat Completions",
        "responses"                 => "Responses",
        "embeddings"                => "Embeddings",
        "images"                    => "Images",
        "audio_speeches"            => "Text to Speech",
        "audio_transcriptions"      => "Transcription",
        "moderations"               => "Moderation",
        "vector_stores"             => "Vector Stores",
        "code_interpreter_sessions" => "Code Interpreter",
        _                           => endpoint
    };
}
