using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using AI_Usage_Dashboard.Data;
using AI_Usage_Dashboard.Models;
using AI_Usage_Dashboard.Services;
using AI_Usage_Dashboard.Utils;

namespace AI_Usage_Dashboard.Controllers;

[ApiController]
[Route("v1/budgets")]
public sealed class BudgetController(MongoDbContext db, BudgetAlertService budgetSvc) : ControllerBase
{
    [HttpGet]
    public async Task<BudgetListResponse> GetBudgets([FromQuery] string? orgId)
    {
        var items = await db.Budgets.Find(Builders<Budget>.Filter.Empty).SortBy(x => x.ProjectName).ToListAsync();
        var summary = new BudgetSummary
        {
            Critical = items.Count(b => string.Equals(b.Level, "critical", StringComparison.OrdinalIgnoreCase)),
            Warning  = items.Count(b => string.Equals(b.Level, "warning",  StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(b.Level, "high",     StringComparison.OrdinalIgnoreCase)),
            Ok       = items.Count(b => string.Equals(b.Level, "ok",       StringComparison.OrdinalIgnoreCase))
        };
        return new BudgetListResponse { Items = items, Summary = summary };
    }

    [HttpPut("{projectId}")]
    public async Task<ActionResult<Budget>> UpdateBudget(string projectId, [FromBody] BudgetUpdateRequest req)
    {
        var existing = await db.Budgets.Find(Builders<Budget>.Filter.Eq(x => x.ProjectId, projectId)).FirstOrDefaultAsync();
        if (existing is null) return NotFound(new { message = $"Budget for project '{projectId}' not found." });

        existing.MonthlyBudget = req.MonthlyBudget;
        existing.UpdatedAt     = DateTime.UtcNow;
        await db.Budgets.ReplaceOneAsync(Builders<Budget>.Filter.Eq(x => x.ProjectId, projectId), existing);

        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        await budgetSvc.RecalculateAsync([projectId], monthStart);
        return Ok(await db.Budgets.Find(Builders<Budget>.Filter.Eq(x => x.ProjectId, projectId)).FirstOrDefaultAsync());
    }
}

[ApiController]
[Route("v1/alerts")]
public sealed class AlertController(MongoDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<List<AlertEvent>> GetAlerts(
        [FromQuery] string? orgId,
        [FromQuery] string? period   = null,
        [FromQuery] string? startDate = null,
        [FromQuery] string? endDate   = null,
        [FromQuery] int     limit     = 50)
    {
        limit = Math.Clamp(limit, 1, 200);
        var (sd, ed) = DateRangeHelper.ResolvePeriod(period, startDate, endDate);
        var f = Builders<AlertEvent>.Filter;
        var filter = f.And(f.Gte(x => x.Timestamp, sd), f.Lt(x => x.Timestamp, ed));
        return await db.AlertEvents.Find(filter).SortByDescending(x => x.Timestamp).Limit(limit).ToListAsync();
    }
}

[ApiController]
[Route("v1/export")]
public sealed class ExportController(ExportJobService exportSvc) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ExportJobStatusResponse>> CreateJob([FromBody] ExportRequest req)
    {
        var jobId = await exportSvc.CreateJobAsync(req);
        return Accepted(new ExportJobStatusResponse { JobId = jobId, Status = "pending" });
    }

    [HttpGet("{jobId}")]
    public async Task<ActionResult<ExportJobStatusResponse>> GetJob(string jobId)
    {
        var job = await exportSvc.GetJobAsync(jobId);
        if (job is null) return NotFound(new { message = $"Job '{jobId}' not found." });
        return Ok(new ExportJobStatusResponse
        {
            JobId        = job.JobId,
            Status       = job.Status,
            DownloadUrl  = job.DownloadUrl,
            ErrorMessage = job.ErrorMessage,
            CreatedAt    = job.CreatedAt,
            CompletedAt  = job.CompletedAt
        });
    }
}

[ApiController]
[Route("v1")]
public sealed class ProjectsController(MongoDbContext db) : ControllerBase
{
    [HttpGet("orgs")]
    public async Task<List<OrgDto>> GetOrgs([FromQuery] string? source = "openai")
    {
        var src = NormalizeSource(source);
        var ct  = HttpContext.RequestAborted;

        if (src == "azure")
        {
            var subs = await db.AzureSubscriptionsRaw.Find(Builders<BsonDocument>.Filter.Empty)
                .Sort(Builders<BsonDocument>.Sort.Ascending("displayName")).ToListAsync(ct);
            return subs.Select(s => new OrgDto
            {
                OrgId   = s.GetValue("subscriptionId", BsonString.Empty).AsString,
                OrgName = s.GetValue("displayName",     s.GetValue("subscriptionId", BsonString.Empty)).AsString
            }).Where(x => !string.IsNullOrWhiteSpace(x.OrgId)).ToList();
        }

        var orgs = await db.OpenAiOrgsRaw.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync(ct);
        return orgs.Select(o =>
        {
            var id      = o.GetValue("_id", BsonString.Empty).AsString;
            var orgName = o.GetValue("title", o.GetValue("name", id)).AsString;
            var projs = new List<ProjectDto>();
            if (o.TryGetValue("projects", out var pEl) && pEl.BsonType == BsonType.Document)
                if (pEl.AsBsonDocument.TryGetValue("data", out var dEl) && dEl.BsonType == BsonType.Array)
                    foreach (var p in dEl.AsBsonArray)
                    {
                        if (p.BsonType != BsonType.Document) continue;
                        var pd = p.AsBsonDocument;
                        projs.Add(new ProjectDto
                        {
                            ProjectId   = pd.GetValue("id", "").AsString,
                            ProjectName = pd.GetValue("title", pd.GetValue("id", "")).AsString
                        });
                    }
            return new OrgDto { OrgId = id, OrgName = orgName, Projects = projs.OrderBy(p => p.ProjectName).ToList() };
        })
        .OrderByDescending(x => x.Projects.Count)
        .ToList();
    }

    [HttpGet("projects")]
    public async Task<List<ProjectDto>> GetProjects([FromQuery] string? source = "openai", [FromQuery] string? orgId = null)
    {
        var src = NormalizeSource(source);
        var ct  = HttpContext.RequestAborted;

        if (src == "azure")
        {
            var f = string.IsNullOrWhiteSpace(orgId)
                ? Builders<BsonDocument>.Filter.Empty
                : Builders<BsonDocument>.Filter.Eq("subscriptionId", orgId);
            var accts = await db.AzureAccountsRaw.Find(f).ToListAsync(ct);
            return accts.Select(a =>
            {
                var name = a.GetValue("name", BsonString.Empty).AsString;
                return new ProjectDto { ProjectId = name, ProjectName = name };
            }).Where(x => !string.IsNullOrWhiteSpace(x.ProjectId))
              .OrderBy(x => x.ProjectName).ToList();
        }

        var orgs = await db.OpenAiOrgsRaw.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync(ct);
        return orgs.SelectMany(o =>
        {
            if (!o.TryGetValue("projects", out var pEl) || pEl.BsonType != BsonType.Document)
                return Enumerable.Empty<ProjectDto>();
            if (!pEl.AsBsonDocument.TryGetValue("data", out var dEl) || dEl.BsonType != BsonType.Array)
                return Enumerable.Empty<ProjectDto>();
            return dEl.AsBsonArray.Where(x => x.BsonType == BsonType.Document)
                .Select(x =>
                {
                    var pd = x.AsBsonDocument;
                    return new ProjectDto
                    {
                        ProjectId   = pd.GetValue("id", "").AsString,
                        ProjectName = pd.GetValue("title", pd.GetValue("id", "")).AsString
                    };
                });
        })
        .Where(x => !string.IsNullOrWhiteSpace(x.ProjectId))
        .OrderBy(x => x.ProjectName)
        .ToList();
    }

    private static string NormalizeSource(string? source)
    {
        var v = (source ?? "openai").Trim().ToLowerInvariant();
        return v == "azure" ? "azure" : "openai";
    }
}

[ApiController]
[Route("v1/models")]
public sealed class ModelsController(MongoDbContext db, DeprecationCatalogService deprecation,
                                     UsageReadService usageReadService) : ControllerBase
{
    [HttpGet("deprecated")]
    public async Task<DeprecationSummary> GetDeprecated(
        [FromQuery] string? orgId,
        [FromQuery] string? projectId,
        [FromQuery] string? period    = null,
        [FromQuery] string? startDate = null,
        [FromQuery] string? endDate   = null,
        [FromQuery] string? source    = "all",
        [FromQuery] string? sortBy    = "daysUntilShutdown",
        [FromQuery] string? sortDir   = "asc")
    {
        var (sd, ed) = DateRangeHelper.ResolvePeriod(period, startDate, endDate);
        var src = NormalizeSource(source);
        var ct  = HttpContext.RequestAborted;
        var today = DateTime.UtcNow.Date;
        var snapshot = await deprecation.GetSnapshotAsync(ct);

        var rows = new List<UsageRecordDto>();
        if (src is "all" or "openai")
            rows.AddRange(await usageReadService.AggregateOpenAiUsageAsync(orgId, projectId, null, null, sd, ed, "model", ct));
        if (src is "all" or "azure")
            rows.AddRange(await usageReadService.AggregateAzureUsageAsync(orgId, projectId, null, sd, ed, ct));

        var modelInfos = rows
            .Where(r => DeprecationCatalogService.Lookup(r.Model, snapshot).IsDeprecated)
            .GroupBy(r => r.Model)
            .Select(g =>
            {
                var info = DeprecationCatalogService.Lookup(g.Key, snapshot);
                var days = 9999;
                if (DateTime.TryParse(info.ShutdownDate, out var sh)) days = (int)(sh.Date - today).TotalDays;
                return new DeprecatedModelInfo
                {
                    ModelName         = g.Key,
                    SubstituteModel   = info.ReplacementModel,
                    ShutdownDate      = info.ShutdownDate,
                    DaysUntilShutdown = days,
                    Urgency           = days <= 0 ? "expired" : days <= 30 ? "critical" : days <= 90 ? "warning" : "upcoming",
                    TotalRequests     = g.Sum(x => x.Requests),
                    TotalCostUsd      = g.Sum(x => x.CostUsd),
                    OpenAiProjects    = g.Where(x => x.Source == "openai").Select(x => x.ProjectName).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToList(),
                    AzureProjects     = g.Where(x => x.Source == "azure").Select(x => x.ProjectName).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToList(),
                    LastSeenDate      = g.Max(x => DateTime.Parse(x.Date)).ToString("yyyy-MM-dd")
                };
            }).ToList();

        modelInfos = SortDeprecated(modelInfos, sortBy, sortDir);

        return new DeprecationSummary
        {
            TotalDeprecated = modelInfos.Count,
            Expired         = modelInfos.Count(x => x.Urgency == "expired"),
            Critical        = modelInfos.Count(x => x.Urgency == "critical"),
            Warning         = modelInfos.Count(x => x.Urgency == "warning"),
            Upcoming        = modelInfos.Count(x => x.Urgency == "upcoming"),
            Models          = modelInfos
        };
    }

    [HttpGet("deprecation-catalog")]
    public async Task<List<DeprecationCatalogEntry>> GetDeprecationCatalog() =>
        await db.DeprecationCatalog.Find(Builders<DeprecationCatalogEntry>.Filter.Empty)
            .SortBy(x => x.ShutdownDate).ThenBy(x => x.Model).ToListAsync(HttpContext.RequestAborted);

    [HttpPut("deprecation-catalog/{model}")]
    public async Task<IActionResult> UpsertDeprecationCatalog(string model, [FromBody] DeprecationCatalogEntry body)
    {
        if (string.IsNullOrWhiteSpace(model))                   return BadRequest("model is required.");
        if (string.IsNullOrWhiteSpace(body.ShutdownDate))       return BadRequest("shutdownDate is required.");
        if (string.IsNullOrWhiteSpace(body.ReplacementModel))   return BadRequest("replacementModel is required.");

        var doc = new DeprecationCatalogEntry
        {
            Model            = model.Trim(),
            ShutdownDate     = body.ShutdownDate.Trim(),
            ReplacementModel = body.ReplacementModel.Trim(),
            IsEnabled        = body.IsEnabled,
            UpdatedAt        = DateTime.UtcNow
        };
        await db.DeprecationCatalog.ReplaceOneAsync(
            Builders<DeprecationCatalogEntry>.Filter.Eq(x => x.Model, doc.Model),
            doc, new ReplaceOptions { IsUpsert = true }, HttpContext.RequestAborted);
        return Ok(doc);
    }

    [HttpDelete("deprecation-catalog/{model}")]
    public async Task<IActionResult> DeleteDeprecationCatalog(string model)
    {
        if (string.IsNullOrWhiteSpace(model)) return BadRequest("model is required.");
        var res = await db.DeprecationCatalog.DeleteOneAsync(
            Builders<DeprecationCatalogEntry>.Filter.Eq(x => x.Model, model.Trim()),
            HttpContext.RequestAborted);
        return Ok(new { deleted = res.DeletedCount });
    }

    // Wipes deprecation_catalog and reseeds the canonical default list.
    [HttpPost("deprecation-catalog/rebuild")]
    public async Task<IActionResult> RebuildDeprecationCatalog()
    {
        var inserted = await deprecation.RebuildAsync(HttpContext.RequestAborted);
        return Ok(new { inserted });
    }

    private static List<DeprecatedModelInfo> SortDeprecated(List<DeprecatedModelInfo> rows, string? sortBy, string? sortDir)
    {
        var asc = !string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        var urgencyOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            { ["expired"] = 0, ["critical"] = 1, ["warning"] = 2, ["upcoming"] = 3 };

        IEnumerable<DeprecatedModelInfo> q = (sortBy ?? "daysUntilShutdown").ToLowerInvariant() switch
        {
            "modelname"       => asc ? rows.OrderBy(x => x.ModelName, StringComparer.OrdinalIgnoreCase) : rows.OrderByDescending(x => x.ModelName, StringComparer.OrdinalIgnoreCase),
            "substitutemodel" => asc ? rows.OrderBy(x => x.SubstituteModel, StringComparer.OrdinalIgnoreCase) : rows.OrderByDescending(x => x.SubstituteModel, StringComparer.OrdinalIgnoreCase),
            "shutdowndate"    => asc ? rows.OrderBy(x => x.ShutdownDate, StringComparer.Ordinal) : rows.OrderByDescending(x => x.ShutdownDate, StringComparer.Ordinal),
            "urgency"         => asc ? rows.OrderBy(x => urgencyOrder.GetValueOrDefault(x.Urgency, 99)) : rows.OrderByDescending(x => urgencyOrder.GetValueOrDefault(x.Urgency, 99)),
            "totalrequests"   => asc ? rows.OrderBy(x => x.TotalRequests) : rows.OrderByDescending(x => x.TotalRequests),
            "lastseendate"    => asc ? rows.OrderBy(x => x.LastSeenDate, StringComparer.Ordinal) : rows.OrderByDescending(x => x.LastSeenDate, StringComparer.Ordinal),
            _                 => asc ? rows.OrderBy(x => x.DaysUntilShutdown).ThenByDescending(x => x.TotalRequests) : rows.OrderByDescending(x => x.DaysUntilShutdown).ThenByDescending(x => x.TotalRequests)
        };
        return q.ToList();
    }

    private static string NormalizeSource(string? source)
    {
        var v = (source ?? "all").Trim().ToLowerInvariant();
        return v is "openai" or "azure" ? v : "all";
    }
}

[ApiController]
[Route("v1/azure")]
public sealed class AzureController(MongoDbContext db, NameLookupService nameLookup) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus([FromQuery] string? subscriptionId = null)
    {
        var ct = HttpContext.RequestAborted;
        // Each collection's per-subscription filter (when applicable). openai_* collections
        // have no subscriptionId concept; we only show azure_* counts on the Azure page,
        // so filter only those when a subscription is chosen.
        var azureCollections = new[]
        {
            "azure_subscriptions_raw","azure_locations_raw","azure_accounts_raw","azure_deployments_raw",
            "azure_usages_raw","azure_metric_defs_raw"
        };
        var openAiCollections = new[]
        {
            "openai_usage_raw","openai_costs_raw","openai_orgs_raw","openai_users_raw","openai_api_keys_raw"
        };
        var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in openAiCollections)
            counts[name] = await db.Database.GetCollection<BsonDocument>(name)
                .CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken: ct);

        FilterDefinition<BsonDocument> azureFilter = string.IsNullOrWhiteSpace(subscriptionId)
            ? Builders<BsonDocument>.Filter.Empty
            : Builders<BsonDocument>.Filter.Eq("subscriptionId", subscriptionId);
        foreach (var name in azureCollections)
        {
            // azure_subscriptions_raw uses subscriptionId as _id, not as a separate field;
            // azure_metric_defs_raw doesn't carry subscriptionId at all. azure_locations_raw
            // is keyed by location with subscriptionId for context but is small. Skip the
            // filter for these so the count still makes sense when a sub is selected.
            var f = (name is "azure_subscriptions_raw" or "azure_metric_defs_raw")
                ? Builders<BsonDocument>.Filter.Empty
                : azureFilter;
            counts[name] = await db.Database.GetCollection<BsonDocument>(name)
                .CountDocumentsAsync(f, cancellationToken: ct);
        }

        var checkpoints = await db.FetchCheckpoints.Find(Builders<FetchCheckpoint>.Filter.Empty).ToListAsync(ct);
        return Ok(new { counts, checkpoints });
    }

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview(
        [FromQuery] int     limit          = 20,
        [FromQuery] string? subscriptionId = null)
    {
        var safe = Math.Clamp(limit, 1, 200);
        var ct = HttpContext.RequestAborted;

        var matchFilter = string.IsNullOrWhiteSpace(subscriptionId)
            ? Builders<BsonDocument>.Filter.Empty
            : Builders<BsonDocument>.Filter.Eq("subscriptionId", subscriptionId);

        var totalRows = await db.AzureCostRaw.CountDocumentsAsync(matchFilter, cancellationToken: ct);
        var pipelineStages = new List<BsonDocument>();
        if (!string.IsNullOrWhiteSpace(subscriptionId))
            pipelineStages.Add(new BsonDocument("$match", new BsonDocument("subscriptionId", subscriptionId)));
        pipelineStages.Add(new BsonDocument("$group", new BsonDocument
        {
            { "_id", new BsonDocument
                {
                    { "subscriptionName", "$subscriptionName" },
                    { "resourceId", "$resourceId" }
                } },
            { "totalCostUsd", new BsonDocument("$sum", new BsonDocument("$toDecimal", new BsonDocument("$ifNull", new BsonArray { "$costUSD", 0 }))) }
        }));
        pipelineStages.Add(new BsonDocument("$sort", new BsonDocument("totalCostUsd", -1)));
        pipelineStages.Add(new BsonDocument("$limit", Math.Min(20, safe)));
        var top = await db.AzureCostRaw.Aggregate<BsonDocument>(pipelineStages, cancellationToken: ct).ToListAsync(ct);

        return Ok(new
        {
            totalRows,
            topAccounts = top.Select(t => new
            {
                subscriptionName = t["_id"].AsBsonDocument.GetValue("subscriptionName", "").AsString,
                resourceId       = t["_id"].AsBsonDocument.GetValue("resourceId", "").AsString,
                totalCostUsd     = t.GetValue("totalCostUsd", BsonDecimal128.Create(0m)).ToDecimal()
            })
        });
    }

    [HttpGet("query")]
    public async Task<IActionResult> Query(
        [FromQuery] string  dataset        = "accounts",
        [FromQuery] int     page           = 1,
        [FromQuery] int     pageSize       = 20,
        [FromQuery] string? search         = null,
        [FromQuery] string? subscriptionId = null,
        [FromQuery] string? accountName    = null,
        [FromQuery] string? period         = null,
        [FromQuery] string? startDate      = null,
        [FromQuery] string? endDate        = null)
    {
        var ds = (dataset ?? "accounts").Trim().ToLowerInvariant();
        var ct = HttpContext.RequestAborted;
        var safePage = Math.Max(1, page);
        var safePageSize = Math.Clamp(pageSize, 1, 200);
        var subNames = await nameLookup.GetSubscriptionNamesAsync(ct);

        if (ds == "modelusage")
        {
            var (from, to) = DateRangeHelper.ResolvePeriod(period, startDate, endDate);
            return Ok(await QueryModelUsageAsync(
                safePage, safePageSize, search, subscriptionId, accountName, from, to, subNames, ct));
        }

        IMongoCollection<BsonDocument> collection = ds switch
        {
            "deployments" => db.AzureDeploymentsRaw,
            _             => db.AzureAccountsRaw
        };

        var filters = new List<FilterDefinition<BsonDocument>>();
        if (!string.IsNullOrWhiteSpace(subscriptionId)) filters.Add(Builders<BsonDocument>.Filter.Eq("subscriptionId", subscriptionId));
        if (!string.IsNullOrWhiteSpace(accountName))    filters.Add(Builders<BsonDocument>.Filter.Eq("accountName",    accountName));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var rx = new BsonRegularExpression(System.Text.RegularExpressions.Regex.Escape(search), "i");
            filters.Add(Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Regex("name",        rx),
                Builders<BsonDocument>.Filter.Regex("accountName", rx),
                Builders<BsonDocument>.Filter.Regex("modelName",   rx)));
        }
        var filter = filters.Count > 0 ? Builders<BsonDocument>.Filter.And(filters) : Builders<BsonDocument>.Filter.Empty;

        var total = await collection.CountDocumentsAsync(filter, cancellationToken: ct);
        var docs  = await collection.Find(filter)
            .Skip((safePage - 1) * safePageSize)
            .Limit(safePageSize)
            .ToListAsync(ct);

        // Deployment docs don't carry `location` — only the parent account does.
        // Build a (subscriptionId, accountName) → location map so deployments can
        // surface a region.
        Dictionary<(string Sub, string Account), string>? accountLocations = null;
        if (ds == "deployments")
            accountLocations = await GetAccountLocationsAsync(ct);

        // Raw BsonDocument cannot be serialized by System.Text.Json — STJ reflects
        // over BsonValue.As* properties (AsBoolean, AsString, ...) and the As-getters
        // throw InvalidCastException when the underlying type doesn't match. Project
        // each row to a plain dictionary with only the columns the frontend renders.
        var rows = docs.Select(d => ProjectRow(ds, d, subNames, accountLocations)).ToList();

        return Ok(new
        {
            data       = rows,
            total      = total,
            page       = safePage,
            pageSize   = safePageSize,
            totalPages = (int)Math.Ceiling((double)total / safePageSize)
        });
    }

    // azure_metrics_raw stores ONE row per (metricName, deployment, day). The frontend
    // renders pivoted columns (requests / inputTokens / outputTokens), so we group by
    // (subscription, account, deployment, model, day) and bucket each metric into the
    // matching column via regex on metricName — same patterns used in
    // UsageReadService.AggregateAzureUsageAsync.
    private async Task<object> QueryModelUsageAsync(
        int page, int pageSize, string? search, string? subscriptionId, string? accountName,
        DateTime from, DateTime to,
        Dictionary<string, string> subNames, CancellationToken ct)
    {
        var match = new BsonDocument
        {
            { "dateUtc", new BsonDocument { { "$gte", from }, { "$lt", to } } }
        };
        if (!string.IsNullOrWhiteSpace(subscriptionId)) match["subscriptionId"] = subscriptionId;
        if (!string.IsNullOrWhiteSpace(accountName))    match["accountName"]    = accountName;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var rx = new BsonRegularExpression(System.Text.RegularExpressions.Regex.Escape(search), "i");
            match["$or"] = new BsonArray
            {
                new BsonDocument("accountName",    rx),
                new BsonDocument("deploymentName", rx),
                new BsonDocument("modelName",      rx),
            };
        }

        BsonDocument MetricSum(string regex) => new("$sum", new BsonDocument("$cond",
            new BsonDocument
            {
                { "if",   new BsonDocument("$regexMatch", new BsonDocument
                    {
                        { "input",   "$metricName" },
                        { "regex",   regex },
                        { "options", "i" }
                    }) },
                { "then", new BsonDocument("$ifNull", new BsonArray { "$total", 0 }) },
                { "else", 0 }
            }));

        // modelName intentionally NOT in the group key. Azure Monitor returns the
        // same (account, deployment, day) split across multiple metric rows; some
        // expose the ModelName dimension ("gpt-5.1"), others don't (""). Putting
        // modelName in the key would split each deployment-day into two rows whose
        // token counts overlap. Instead group only by (sub, account, deployment, day)
        // and pick the non-empty modelName via $max (empty string sorts smallest).
        var groupStage = new BsonDocument("$group", new BsonDocument
        {
            { "_id", new BsonDocument
                {
                    { "subscriptionId", "$subscriptionId" },
                    { "accountName",    "$accountName" },
                    { "deploymentName", "$deploymentName" },
                    { "dateUtc",        "$dateUtc" },
                } },
            { "modelName",    new BsonDocument("$max", "$modelName") },
            { "requests",     MetricSum("Requests|ModelRequests") },
            { "inputTokens",  MetricSum("InputTokens|PromptTokens|ProcessedPromptTokens|AudioInputTokens") },
            { "outputTokens", MetricSum("OutputTokens|CompletionTokens|GeneratedTokens|AudioOutputTokens") },
        });

        var basePipeline = new List<BsonDocument>
        {
            new("$match", match),
            groupStage,
            new("$match", new BsonDocument("$or", new BsonArray
            {
                new BsonDocument("requests",     new BsonDocument("$gt", 0)),
                new BsonDocument("inputTokens",  new BsonDocument("$gt", 0)),
                new BsonDocument("outputTokens", new BsonDocument("$gt", 0)),
            })),
        };

        // Total = number of rows after grouping + filter (count via $count stage).
        var countPipeline = new List<BsonDocument>(basePipeline) { new("$count", "n") };
        var countDoc = (await db.AzureMetricsRaw
            .Aggregate<BsonDocument>(countPipeline, cancellationToken: ct)
            .ToListAsync(ct)).FirstOrDefault();
        var total = countDoc is null ? 0L : countDoc.GetValue("n", BsonInt64.Create(0L)).ToInt64();

        var pagePipeline = new List<BsonDocument>(basePipeline)
        {
            new("$sort", new BsonDocument
            {
                { "_id.dateUtc",        -1 },
                { "_id.accountName",     1 },
                { "_id.deploymentName",  1 },
            }),
            new("$skip",  (page - 1) * pageSize),
            new("$limit", pageSize),
        };
        var docs = await db.AzureMetricsRaw
            .Aggregate<BsonDocument>(pagePipeline, cancellationToken: ct)
            .ToListAsync(ct);

        var rows = docs.Select(d =>
        {
            var key = d["_id"].AsBsonDocument;
            var subId = key.GetValue("subscriptionId", BsonString.Empty).AsString;
            var deployment = key.GetValue("deploymentName", BsonString.Empty).AsString;
            var modelName  = d.GetValue("modelName", BsonString.Empty).AsString;
            if (string.IsNullOrWhiteSpace(modelName)) modelName = deployment;
            return new Dictionary<string, object?>
            {
                ["subscriptionName"] = subNames.TryGetValue(subId, out var n) && !string.IsNullOrWhiteSpace(n) ? n : subId,
                ["accountName"]      = key.GetValue("accountName", BsonString.Empty).AsString,
                ["deploymentName"]   = deployment,
                ["modelName"]        = modelName,
                ["requests"]         = d.GetValue("requests",     BsonInt64.Create(0L)).ToDouble(),
                ["inputTokens"]      = d.GetValue("inputTokens",  BsonInt64.Create(0L)).ToDouble(),
                ["outputTokens"]     = d.GetValue("outputTokens", BsonInt64.Create(0L)).ToDouble(),
                ["dateUtc"]          = key.GetValue("dateUtc", BsonNull.Value) is { IsValidDateTime: true } dt
                                            ? dt.ToUniversalTime().ToString("o") : null,
            };
        }).ToList();

        return new
        {
            data       = rows,
            total      = total,
            page       = page,
            pageSize   = pageSize,
            totalPages = (int)Math.Ceiling((double)total / pageSize),
        };
    }

    private static Dictionary<string, object?> ProjectRow(
        string dataset, BsonDocument d, Dictionary<string, string> subNames,
        Dictionary<(string Sub, string Account), string>? accountLocations)
    {
        string Str(string k) => d.TryGetValue(k, out var v) && v.IsString ? v.AsString : string.Empty;
        string SubName()
        {
            var id = Str("subscriptionId");
            return subNames.TryGetValue(id, out var n) && !string.IsNullOrWhiteSpace(n) ? n : id;
        }

        return dataset switch
        {
            "deployments" => new Dictionary<string, object?>
            {
                ["subscriptionName"] = SubName(),
                ["resourceGroup"]    = Str("resourceGroup"),
                ["accountName"]      = Str("accountName"),
                ["deploymentName"]   = Str("name"),
                ["modelName"]        = ReadNested(d, "properties", "model", "name"),
                ["modelVersion"]     = ReadNested(d, "properties", "model", "version"),
                ["region"]           = accountLocations is not null
                                            && accountLocations.TryGetValue((Str("subscriptionId"), Str("accountName")), out var loc)
                                            ? loc : string.Empty,
                ["status"]           = ReadNested(d, "properties", "provisioningState"),
                ["provisioningState"]= ReadNested(d, "properties", "provisioningState"),
            },
            _ /* accounts */ => new Dictionary<string, object?>
            {
                ["subscriptionName"] = SubName(),
                ["resourceGroup"]    = ExtractResourceGroup(Str("id")),
                ["accountName"]      = Str("name"),
                ["kind"]             = Str("kind"),
                ["region"]           = Str("location"),
                ["scanStatus"]       = ReadNested(d, "properties", "provisioningState"),
            },
        };
    }

    private async Task<Dictionary<(string Sub, string Account), string>> GetAccountLocationsAsync(CancellationToken ct)
    {
        var docs = await db.AzureAccountsRaw
            .Find(Builders<BsonDocument>.Filter.Empty)
            .Project(Builders<BsonDocument>.Projection
                .Include("subscriptionId").Include("name").Include("location"))
            .ToListAsync(ct);
        var map = new Dictionary<(string, string), string>();
        foreach (var d in docs)
        {
            var sub  = d.GetValue("subscriptionId", BsonString.Empty).AsString;
            var name = d.GetValue("name",           BsonString.Empty).AsString;
            var loc  = d.GetValue("location",       BsonString.Empty).AsString;
            if (!string.IsNullOrEmpty(sub) && !string.IsNullOrEmpty(name))
                map[(sub, name)] = loc;
        }
        return map;
    }

    private static string ReadNested(BsonDocument d, params string[] path)
    {
        BsonValue current = d;
        foreach (var key in path)
        {
            if (current is null || current.BsonType != BsonType.Document) return string.Empty;
            if (!current.AsBsonDocument.TryGetValue(key, out var next)) return string.Empty;
            current = next;
        }
        return current is { BsonType: BsonType.String } v ? v.AsString : string.Empty;
    }

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

[ApiController]
[Route("v1/maintenance")]
public sealed class MaintenanceController(MongoDbContext db) : ControllerBase
{
    [HttpPost("reset-today")]
    public async Task<IActionResult> ResetToday()
    {
        var target = DateTime.UtcNow.Date.AddDays(-1);
        foreach (var s in new[] { "openai_usage", "openai_costs", "azure_snapshot" })
            await UpsertCheckpointAsync(s, target);
        return Ok(new { message = "checkpoints_reset_to_yesterday", targetUtc = target.ToString("o") });
    }

    [HttpPost("backfill")]
    public async Task<IActionResult> Backfill([FromQuery] int months = 6)
    {
        var safe = Math.Clamp(months, 1, 24);
        var target = DateTime.UtcNow.AddMonths(-safe);
        foreach (var s in new[] { "openai_usage", "openai_costs", "azure_snapshot" })
            await UpsertCheckpointAsync(s, target);
        return Ok(new { message = "checkpoints_updated", months = safe, targetUtc = target.ToString("o") });
    }

    [HttpPost("full-reset")]
    public async Task<IActionResult> FullReset()
    {
        var res = await db.FetchCheckpoints.DeleteManyAsync(Builders<FetchCheckpoint>.Filter.Empty, HttpContext.RequestAborted);
        return Ok(new { message = "all_checkpoints_deleted", deleted = res.DeletedCount });
    }

    [HttpGet("checkpoints")]
    public async Task<IActionResult> GetCheckpoints()
    {
        var rows = await db.FetchCheckpoints.Find(Builders<FetchCheckpoint>.Filter.Empty)
            .SortBy(x => x.Source).ToListAsync(HttpContext.RequestAborted);
        return Ok(rows.Select(x => new { source = x.Source, lastFetchedAtUtc = x.LastFetchedAt.ToString("o") }));
    }

    // GET /v1/maintenance/logs?level=warn|error&category=&search=&since=2026-04-29T00:00:00Z&page=1&pageSize=50
    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs(
        [FromQuery] string? level    = null,
        [FromQuery] string? category = null,
        [FromQuery] string? search   = null,
        [FromQuery] DateTime? since  = null,
        [FromQuery] int page         = 1,
        [FromQuery] int pageSize     = 50)
    {
        var ct = HttpContext.RequestAborted;
        var safePage     = Math.Max(1, page);
        var safePageSize = Math.Clamp(pageSize, 1, 200);

        var col = db.Database.GetCollection<BsonDocument>("system_logs");
        var filters = new List<FilterDefinition<BsonDocument>>();

        if (!string.IsNullOrWhiteSpace(level))
            filters.Add(Builders<BsonDocument>.Filter.Eq("level", level.Trim().ToLowerInvariant()));
        if (!string.IsNullOrWhiteSpace(category))
            filters.Add(Builders<BsonDocument>.Filter.Regex("category",
                new BsonRegularExpression(System.Text.RegularExpressions.Regex.Escape(category), "i")));
        if (since.HasValue)
            filters.Add(Builders<BsonDocument>.Filter.Gte("timestamp",
                DateTime.SpecifyKind(since.Value, DateTimeKind.Utc)));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var rx = new BsonRegularExpression(System.Text.RegularExpressions.Regex.Escape(search), "i");
            filters.Add(Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Regex("message",   rx),
                Builders<BsonDocument>.Filter.Regex("exception", rx)));
        }

        var filter = filters.Count > 0
            ? Builders<BsonDocument>.Filter.And(filters)
            : Builders<BsonDocument>.Filter.Empty;

        var total = await col.CountDocumentsAsync(filter, cancellationToken: ct);
        var docs  = await col.Find(filter)
            .Sort(Builders<BsonDocument>.Sort.Descending("timestamp"))
            .Skip((safePage - 1) * safePageSize)
            .Limit(safePageSize)
            .ToListAsync(ct);

        return Ok(new PaginatedResponse<BsonDocument>
        {
            Data = docs, Total = total, Page = safePage, PageSize = safePageSize,
            TotalPages = (int)Math.Ceiling((double)total / safePageSize)
        });
    }

    // GET /v1/maintenance/logs/summary — breakdown by (level, category) for the last 24h
    [HttpGet("logs/summary")]
    public async Task<IActionResult> GetLogsSummary([FromQuery] int hours = 24)
    {
        var ct = HttpContext.RequestAborted;
        var since = DateTime.UtcNow.AddHours(-Math.Clamp(hours, 1, 24 * 30));
        var col = db.Database.GetCollection<BsonDocument>("system_logs");
        var pipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument("timestamp", new BsonDocument("$gte", since))),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument { { "level", "$level" }, { "category", "$category" } } },
                { "count", new BsonDocument("$sum", 1) },
                { "lastSeen", new BsonDocument("$max", "$timestamp") }
            }),
            new BsonDocument("$sort", new BsonDocument("count", -1))
        };
        var rows = await col.Aggregate<BsonDocument>(pipeline, cancellationToken: ct).ToListAsync(ct);
        return Ok(new
        {
            sinceUtc = since.ToString("o"),
            items = rows.Select(r => new
            {
                level    = r["_id"].AsBsonDocument.GetValue("level",    "").AsString,
                category = r["_id"].AsBsonDocument.GetValue("category", "").AsString,
                count    = r.GetValue("count",    0).ToInt64(),
                lastSeen = r.GetValue("lastSeen", BsonNull.Value).IsBsonNull ? null
                          : (string?)r["lastSeen"].ToUniversalTime().ToString("o")
            })
        });
    }

    private Task UpsertCheckpointAsync(string source, DateTime targetUtc)
    {
        var doc = new FetchCheckpoint { Source = source, LastFetchedAt = DateTime.SpecifyKind(targetUtc, DateTimeKind.Utc) };
        return db.FetchCheckpoints.ReplaceOneAsync(
            Builders<FetchCheckpoint>.Filter.Eq(x => x.Source, source),
            doc, new ReplaceOptions { IsUpsert = true }, HttpContext.RequestAborted);
    }
}
