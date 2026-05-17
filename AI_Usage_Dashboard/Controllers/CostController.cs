using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using AI_Usage_Dashboard.Data;
using AI_Usage_Dashboard.Models;
using AI_Usage_Dashboard.Services;
using AI_Usage_Dashboard.Utils;

namespace AI_Usage_Dashboard.Controllers;

[ApiController]
[Route("v1/cost")]
public sealed class CostController(MongoDbContext db, NameLookupService nameLookup, UsageReadService usageReadService) : ControllerBase
{
    [HttpGet("breakdown")]
    public async Task<CostBreakdownResponse> GetBreakdown(
        [FromQuery] string? orgId,
        [FromQuery] string? projectId,
        [FromQuery] string  groupBy   = "project",
        [FromQuery] string? period    = null,
        [FromQuery] string? startDate = null,
        [FromQuery] string? endDate   = null,
        [FromQuery] string? source    = "all",
        [FromQuery] string? sortBy    = "cost",
        [FromQuery] string? sortDir   = "desc")
    {
        var (sd, ed) = DateRangeHelper.ResolvePeriod(period, startDate, endDate);
        var src = NormalizeSource(source);
        var ct  = HttpContext.RequestAborted;

        var key = (groupBy ?? "project").Trim().ToLowerInvariant();
        var merged = new Dictionary<string, CostBreakdownItem>(StringComparer.OrdinalIgnoreCase);

        if (src is "all" or "openai")
        {
            var items = await OpenAiBreakdownAsync(key, projectId, sd, ed, ct);
            foreach (var it in items) MergeItem(merged, it);
        }
        if (src is "all" or "azure")
        {
            var items = await AzureBreakdownAsync(key, orgId, sd, ed, ct);
            foreach (var it in items) MergeItem(merged, it);
        }

        var results = merged.Values.ToList();
        var totalCost = results.Sum(x => x.CostUsd);
        foreach (var r in results)
            r.Percentage = totalCost > 0 ? Math.Round((double)(r.CostUsd / totalCost) * 100, 1) : 0;

        var asc = string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
        var sorted = string.Equals(sortBy, "label", StringComparison.OrdinalIgnoreCase)
            ? (asc ? results.OrderBy(x => x.Label) : results.OrderByDescending(x => x.Label)).ToList()
            : (asc ? results.OrderBy(x => x.CostUsd) : results.OrderByDescending(x => x.CostUsd)).ToList();

        return new CostBreakdownResponse
        {
            Items             = sorted,
            TotalCostUsd      = totalCost,
            TotalRequests     = results.Sum(x => x.Requests),
            TotalInputTokens  = results.Sum(x => x.InputTokens),
            TotalOutputTokens = results.Sum(x => x.OutputTokens)
        };
    }

    [HttpGet("trend-stacked")]
    public async Task<StackedTrendResponse> GetTrendStacked(
        [FromQuery] string? orgId,
        [FromQuery] string? projectId,
        [FromQuery] string? period    = null,
        [FromQuery] string? startDate = null,
        [FromQuery] string? endDate   = null,
        [FromQuery] int     topN      = 10,
        [FromQuery] string? source    = "all")
    {
        var (sd, ed) = DateRangeHelper.ResolvePeriod(period, startDate, endDate);
        if (ed <= sd) ed = sd.AddDays(1);
        var safeTop = Math.Clamp(topN, 3, 20);
        var src = NormalizeSource(source);
        var ct  = HttpContext.RequestAborted;

        var dailyByLabel = new Dictionary<DateTime, Dictionary<string, decimal>>();
        var totals       = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        void Add(DateTime d, string label, decimal c)
        {
            if (c <= 0) return;
            if (!dailyByLabel.TryGetValue(d, out var bs))
                dailyByLabel[d] = bs = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            bs[label] = bs.GetValueOrDefault(label) + c;
            totals[label] = totals.GetValueOrDefault(label) + c;
        }

        if (src is "all" or "openai")
        {
            var matchOpenAi = new BsonDocument { { "date", new BsonDocument { { "$gte", sd }, { "$lt", ed } } } };
            if (!string.IsNullOrWhiteSpace(projectId)) matchOpenAi["projectId"] = projectId!;
            var pipeline = new[]
            {
                new BsonDocument("$match", matchOpenAi),
                new BsonDocument("$group", new BsonDocument
                {
                    { "_id", new BsonDocument { { "date", "$date" }, { "projectId", "$projectId" } } },
                    { "cost", new BsonDocument("$sum", new BsonDocument("$toDecimal", new BsonDocument("$ifNull", new BsonArray { "$amount.value", 0 }))) }
                })
            };
            var rows = await db.OpenAiCostsRaw.Aggregate<BsonDocument>(pipeline, cancellationToken: ct).ToListAsync(ct);
            var projects = await nameLookup.GetProjectNamesAsync(ct);
            foreach (var r in rows)
            {
                var key = r["_id"].AsBsonDocument;
                var d = key.GetValue("date", BsonNull.Value).ToUniversalTime().Date;
                var pid = key.GetValue("projectId", BsonString.Empty).AsString;
                var label = projects.TryGetValue(pid, out var n) && !string.IsNullOrWhiteSpace(n) ? n
                            : (string.IsNullOrWhiteSpace(pid) ? "(unknown)" : pid);
                var cost = r.GetValue("cost", BsonDecimal128.Create(0m)).ToDecimal();
                Add(d, label, cost);
            }
        }

        if (src is "all" or "azure")
        {
            // usageDate is Int64 yyyyMMdd; compare as long to make the index hit.
            var matchAz = new BsonDocument
            {
                { "usageDate", new BsonDocument
                    {
                        { "$gte", long.Parse(sd.ToString("yyyyMMdd")) },
                        { "$lt",  long.Parse(ed.ToString("yyyyMMdd")) }
                    } }
            };
            if (!string.IsNullOrWhiteSpace(orgId)) matchAz["subscriptionId"] = orgId!;
            var pipeline = new[]
            {
                new BsonDocument("$match", matchAz),
                new BsonDocument("$group", new BsonDocument
                {
                    { "_id", new BsonDocument { { "date", "$usageDate" }, { "resourceId", "$resourceId" } } },
                    { "cost", new BsonDocument("$sum", new BsonDocument("$toDecimal", new BsonDocument("$ifNull", new BsonArray { "$costUSD", 0 }))) }
                })
            };
            var rows = await db.AzureCostRaw.Aggregate<BsonDocument>(pipeline, cancellationToken: ct).ToListAsync(ct);
            foreach (var r in rows)
            {
                var key = r["_id"].AsBsonDocument;
                var dateRaw = key.GetValue("date", BsonNull.Value);
                // Either Int64 yyyyMMdd or (legacy) string — handle both.
                long dateNum = dateRaw.IsNumeric ? dateRaw.ToInt64()
                              : (long.TryParse(dateRaw.IsString ? dateRaw.AsString : "0", out var n) ? n : 0L);
                if (dateNum <= 0) continue;
                if (!DateTime.TryParseExact(dateNum.ToString(), "yyyyMMdd", null,
                        System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                        out var d)) continue;
                d = d.Date;
                var rid = key.GetValue("resourceId", BsonString.Empty).AsString;
                var label = ExtractAccount(rid);
                if (string.IsNullOrWhiteSpace(label)) label = "(unknown)";
                var cost = r.GetValue("cost", BsonDecimal128.Create(0m)).ToDecimal();
                Add(d, label, cost);
            }
        }

        var topSet = totals.OrderByDescending(x => x.Value).Take(safeTop).Select(x => x.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var series = totals.OrderByDescending(x => x.Value).Take(safeTop)
            .Select(x => new StackedTrendSeries { Key = x.Key, Label = x.Key, TotalCostUsd = x.Value }).ToList();
        var hasOther = totals.Any(x => !topSet.Contains(x.Key));
        if (hasOther)
            series.Add(new StackedTrendSeries
            {
                Key = "__other", Label = "Other",
                TotalCostUsd = totals.Where(x => !topSet.Contains(x.Key)).Sum(x => x.Value)
            });

        var days = new List<StackedTrendDay>();
        for (var d = sd.Date; d < ed.Date; d = d.AddDays(1))
        {
            dailyByLabel.TryGetValue(d, out var raw);
            raw ??= new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            var bySeries = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            decimal otherCost = 0;
            foreach (var kv in raw)
            {
                if (topSet.Contains(kv.Key)) bySeries[kv.Key] = kv.Value;
                else otherCost += kv.Value;
            }
            if (hasOther && otherCost > 0) bySeries["__other"] = otherCost;
            days.Add(new StackedTrendDay { Date = d.ToString("yyyy-MM-dd"), TotalCostUsd = raw.Values.Sum(), BySeries = bySeries });
        }

        return new StackedTrendResponse
        {
            TotalSpend    = days.Sum(x => x.TotalCostUsd),
            AvgDailySpend = days.Count > 0 ? days.Average(x => x.TotalCostUsd) : 0m,
            Series        = series,
            Days          = days
        };
    }

    // ── OpenAI breakdown by groupBy ──────────────────────────────────────────
    private async Task<List<CostBreakdownItem>> OpenAiBreakdownAsync(
        string key, string? projectId, DateTime sd, DateTime ed, CancellationToken ct)
    {
        var matchCost = new BsonDocument { { "date", new BsonDocument { { "$gte", sd }, { "$lt", ed } } } };
        if (!string.IsNullOrWhiteSpace(projectId)) matchCost["projectId"] = projectId!;
        var costPipeline = new[]
        {
            new BsonDocument("$match", matchCost),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument { { "date", "$date" }, { "projectId", "$projectId" } } },
                { "cost", new BsonDocument("$sum", new BsonDocument("$toDecimal", new BsonDocument("$ifNull", new BsonArray { "$amount.value", 0 }))) }
            })
        };
        var costRows = await db.OpenAiCostsRaw.Aggregate<BsonDocument>(costPipeline, cancellationToken: ct).ToListAsync(ct);
        var costMap  = costRows.ToDictionary(
            r =>
            {
                var k = r["_id"].AsBsonDocument;
                return (Pid: k.GetValue("projectId", BsonString.Empty).AsString,
                        Dt:  k.GetValue("date", BsonNull.Value).ToUniversalTime().Date);
            },
            r => r.GetValue("cost", BsonDecimal128.Create(0m)).ToDecimal());

        var rows = await usageReadService.AggregateOpenAiUsageAsync(
            null, projectId, null, null, sd, ed, groupBy: null, ct);

        Func<UsageRecordDto, string> labelSelector = key switch
        {
            "model"      => x => string.IsNullOrWhiteSpace(x.Model)      ? "(unknown)" : DeprecationCatalogService.StripSnapshotSuffix(x.Model),
            "capability" => x => string.IsNullOrWhiteSpace(x.Capability) ? "(unknown)" : x.Capability,
            "date"       => x => DateTime.Parse(x.Date).ToUniversalTime().Date.ToString("yyyy-MM-dd"),
            _            => x => string.IsNullOrWhiteSpace(x.ProjectName) ? (string.IsNullOrWhiteSpace(x.ProjectId) ? "(unknown)" : x.ProjectId) : x.ProjectName
        };

        return rows
            .GroupBy(labelSelector, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CostBreakdownItem
            {
                Label        = g.Key,
                CostUsd      = g.Sum(x => x.CostUsd),
                Requests     = g.Sum(x => x.Requests),
                InputTokens  = g.Sum(x => x.InputTokens),
                OutputTokens = g.Sum(x => x.OutputTokens)
            }).ToList();
    }

    // ── Azure breakdown by groupBy ───────────────────────────────────────────
    private async Task<List<CostBreakdownItem>> AzureBreakdownAsync(
        string key, string? subscriptionId, DateTime sd, DateTime ed, CancellationToken ct)
    {
        var rows = await usageReadService.AggregateAzureUsageAsync(subscriptionId, null, null, sd, ed, ct);

        // usageDate is Int64 yyyyMMdd in azure_cost_raw — compare as numbers.
        var matchCost = new BsonDocument
        {
            { "usageDate", new BsonDocument
                {
                    { "$gte", long.Parse(sd.ToString("yyyyMMdd")) },
                    { "$lt",  long.Parse(ed.ToString("yyyyMMdd")) }
                } }
        };
        if (!string.IsNullOrWhiteSpace(subscriptionId)) matchCost["subscriptionId"] = subscriptionId!;
        var costPipeline = new[]
        {
            new BsonDocument("$match", matchCost),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id",  "$resourceId" },
                { "cost", new BsonDocument("$sum", new BsonDocument("$toDecimal", new BsonDocument("$ifNull", new BsonArray { "$costUSD", 0 }))) }
            })
        };
        var costsRows = await db.AzureCostRaw.Aggregate<BsonDocument>(costPipeline, cancellationToken: ct).ToListAsync(ct);
        var totalCost = costsRows.Sum(r => r.GetValue("cost", BsonDecimal128.Create(0m)).ToDecimal());

        // When the Azure Monitor row has no model dimension (or Azure returned the
        // "__Empty" placeholder, which we treat as empty at write time), label as
        // "(unknown)" — same convention as the OpenAI side.
        static bool MissingModel(string s) => string.IsNullOrWhiteSpace(s) || s == "__Empty";
        Func<UsageRecordDto, string> labelSelector = key switch
        {
            "model"      => x => MissingModel(x.Model) ? "(unknown)" : DeprecationCatalogService.StripSnapshotSuffix(x.Model),
            "capability" => _ => "Azure OpenAI",
            "date"       => x => DateTime.Parse(x.Date).ToUniversalTime().Date.ToString("yyyy-MM-dd"),
            _            => x => string.IsNullOrWhiteSpace(x.ProjectName) ? "(unknown)" : x.ProjectName
        };

        // Azure Monitor returns the same model name with inconsistent casing
        // across different metric responses (e.g. "Whisper" + "whisper",
        // "DeepSeek-V3.2" + "deepseek-v3.2"). Group case-insensitively so they
        // collapse into a single bar — and to keep weights' OrdinalIgnoreCase
        // dictionary lookup below consistent with the group key.
        var grouped = rows.GroupBy(labelSelector, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CostBreakdownItem
            {
                Label        = g.Key,
                Requests     = g.Sum(x => x.Requests),
                InputTokens  = g.Sum(x => x.InputTokens),
                OutputTokens = g.Sum(x => x.OutputTokens)
            }).ToList();

        // Distribute totalCost across labels by token weight
        var weights = grouped.ToDictionary(x => x.Label, x => x.InputTokens + x.OutputTokens, StringComparer.OrdinalIgnoreCase);
        var totalWeight = weights.Values.Sum();
        if (totalWeight > 0)
            foreach (var g in grouped) g.CostUsd = totalCost * weights[g.Label] / totalWeight;
        else if (grouped.Count > 0)
            grouped[0].CostUsd = totalCost;

        return grouped;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static void MergeItem(IDictionary<string, CostBreakdownItem> target, CostBreakdownItem item)
    {
        if (!target.TryGetValue(item.Label, out var current))
        {
            target[item.Label] = new CostBreakdownItem
            {
                Label        = item.Label,
                CostUsd      = item.CostUsd,
                Requests     = item.Requests,
                InputTokens  = item.InputTokens,
                OutputTokens = item.OutputTokens
            };
            return;
        }
        current.CostUsd      += item.CostUsd;
        current.Requests     += item.Requests;
        current.InputTokens  += item.InputTokens;
        current.OutputTokens += item.OutputTokens;
    }

    private static string ExtractAccount(string resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId)) return string.Empty;
        var parts = resourceId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length - 1; i++)
            if (parts[i].Equals("accounts", StringComparison.OrdinalIgnoreCase))
                return parts[i + 1];
        return string.Empty;
    }

    private static string NormalizeSource(string? source)
    {
        var v = (source ?? "all").Trim().ToLowerInvariant();
        return v is "openai" or "azure" ? v : "all";
    }
}
