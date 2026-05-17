using MongoDB.Bson;
using MongoDB.Driver;
using AI_Usage_Dashboard.Models;

namespace AI_Usage_Dashboard.Data;

// Raw API responses go into BsonDocument collections (one per source endpoint).
// The only typed collections are entities that are NOT derived from upstream APIs:
// budgets / alert_events / export_jobs / fetch_checkpoints / deprecation_catalog.
public sealed class MongoDbContext
{
    private readonly IMongoDatabase _db;

    public MongoDbContext(IConfiguration config)
    {
        var connectionString = config["MongoDB:ConnectionString"] ?? "mongodb://localhost:27017";
        var databaseName     = config["MongoDB:Database"] ?? "ai_usage_dashboard";
        _db = new MongoClient(connectionString).GetDatabase(databaseName);
    }

    public IMongoDatabase Database => _db;

    // ── Typed collections (user / metadata) ──────────────────────────────────
    public IMongoCollection<Budget>                  Budgets            => _db.GetCollection<Budget>("budgets");
    public IMongoCollection<AlertEvent>              AlertEvents        => _db.GetCollection<AlertEvent>("alert_events");
    public IMongoCollection<ExportJob>               ExportJobs         => _db.GetCollection<ExportJob>("export_jobs");
    public IMongoCollection<FetchCheckpoint>         FetchCheckpoints   => _db.GetCollection<FetchCheckpoint>("fetch_checkpoints");
    public IMongoCollection<DeprecationCatalogEntry> DeprecationCatalog => _db.GetCollection<DeprecationCatalogEntry>("deprecation_catalog");

    // ── Raw API collections (BsonDocument; worker writes raw JSON) ───────────
    public IMongoCollection<BsonDocument> OpenAiUsageRaw       => _db.GetCollection<BsonDocument>("openai_usage_raw");
    public IMongoCollection<BsonDocument> OpenAiCostsRaw       => _db.GetCollection<BsonDocument>("openai_costs_raw");
    public IMongoCollection<BsonDocument> OpenAiOrgsRaw        => _db.GetCollection<BsonDocument>("openai_orgs_raw");
    public IMongoCollection<BsonDocument> OpenAiUsersRaw       => _db.GetCollection<BsonDocument>("openai_users_raw");
    public IMongoCollection<BsonDocument> OpenAiApiKeysRaw     => _db.GetCollection<BsonDocument>("openai_api_keys_raw");

    public IMongoCollection<BsonDocument> AzureSubscriptionsRaw => _db.GetCollection<BsonDocument>("azure_subscriptions_raw");
    public IMongoCollection<BsonDocument> AzureLocationsRaw     => _db.GetCollection<BsonDocument>("azure_locations_raw");
    public IMongoCollection<BsonDocument> AzureAccountsRaw      => _db.GetCollection<BsonDocument>("azure_accounts_raw");
    public IMongoCollection<BsonDocument> AzureDeploymentsRaw   => _db.GetCollection<BsonDocument>("azure_deployments_raw");
    public IMongoCollection<BsonDocument> AzureUsagesRaw        => _db.GetCollection<BsonDocument>("azure_usages_raw");
    public IMongoCollection<BsonDocument> AzureMetricDefsRaw    => _db.GetCollection<BsonDocument>("azure_metric_defs_raw");
    public IMongoCollection<BsonDocument> AzureMetricsRaw       => _db.GetCollection<BsonDocument>("azure_metrics_raw");
    public IMongoCollection<BsonDocument> AzureCostRaw          => _db.GetCollection<BsonDocument>("azure_cost_raw");

    public async Task EnsureIndexesAsync()
    {
        // ── OpenAI raw indexes ────────────────────────────────────────────────
        await OpenAiUsageRaw.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys
                .Ascending("endpoint")
                .Ascending("date")
                .Ascending("projectId")
                .Ascending("model")
                .Ascending("userId")
                .Ascending("apiKeyId")
                .Ascending("batch"),
            new CreateIndexOptions { Unique = true, Name = "openai_usage_unique" }));

        await OpenAiUsageRaw.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Descending("date").Ascending("projectId").Ascending("model"),
            new CreateIndexOptions { Name = "openai_usage_query" }));

        await OpenAiCostsRaw.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys
                .Ascending("date").Ascending("projectId").Ascending("lineItem"),
            new CreateIndexOptions { Unique = true, Name = "openai_costs_unique" }));

        await OpenAiCostsRaw.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Descending("date").Ascending("projectId"),
            new CreateIndexOptions { Name = "openai_costs_query" }));

        // ── Azure raw indexes ─────────────────────────────────────────────────
        // Cost Management Query groups by 7 dimensions; the same (sub, resourceId,
        // meter, usageDate) tuple can repeat across different ResourceLocation /
        // MeterCategory / MeterSubCategory / ServiceName combinations. Include
        // every grouped dimension in the unique key so each grouping combo is its
        // own document and upserts stay idempotent.
        await AzureCostRaw.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys
                .Ascending("subscriptionId")
                .Ascending("resourceId")
                .Ascending("resourceGroup")
                .Ascending("resourceLocation")
                .Ascending("meterCategory")
                .Ascending("meterSubCategory")
                .Ascending("meter")
                .Ascending("serviceName")
                .Ascending("usageDate"),
            new CreateIndexOptions { Unique = true, Name = "azure_cost_unique" }));

        await AzureCostRaw.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Descending("usageDate").Ascending("subscriptionId"),
            new CreateIndexOptions { Name = "azure_cost_query" }));

        await AzureMetricsRaw.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys
                .Ascending("resourceId").Ascending("metricName").Ascending("deploymentName")
                .Ascending("modelName").Ascending("modelVersion").Ascending("region").Ascending("dateUtc"),
            new CreateIndexOptions { Unique = true, Name = "azure_metrics_unique" }));

        await AzureMetricsRaw.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Descending("dateUtc").Ascending("subscriptionId").Ascending("accountName"),
            new CreateIndexOptions { Name = "azure_metrics_query" }));

        await AzureAccountsRaw.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("_id"),
            new CreateIndexOptions { Name = "azure_accounts_id" }));

        await AzureDeploymentsRaw.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("subscriptionId").Ascending("accountName"),
            new CreateIndexOptions { Name = "azure_deployments_account" }));

        await AzureUsagesRaw.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("accountId").Ascending("metricName"),
            new CreateIndexOptions { Unique = true, Name = "azure_usages_unique" }));

        // ── Typed collection indexes ──────────────────────────────────────────
        await AlertEvents.Indexes.CreateOneAsync(new CreateIndexModel<AlertEvent>(
            Builders<AlertEvent>.IndexKeys.Descending(x => x.Timestamp)));

        await AlertEvents.Indexes.CreateOneAsync(new CreateIndexModel<AlertEvent>(
            Builders<AlertEvent>.IndexKeys.Ascending(x => x.ProjectId).Descending(x => x.Timestamp)));

        await DeprecationCatalog.Indexes.CreateOneAsync(new CreateIndexModel<DeprecationCatalogEntry>(
            Builders<DeprecationCatalogEntry>.IndexKeys.Ascending(x => x.IsEnabled).Ascending(x => x.ShutdownDate)));

        // ── system_logs (Warning+ events written by MongoLoggerProvider) ─────
        var systemLogs = _db.GetCollection<MongoDB.Bson.BsonDocument>("system_logs");
        // Auto-expire after 30 days so the collection stays bounded.
        await systemLogs.Indexes.CreateOneAsync(new CreateIndexModel<MongoDB.Bson.BsonDocument>(
            Builders<MongoDB.Bson.BsonDocument>.IndexKeys.Ascending("timestamp"),
            new CreateIndexOptions { ExpireAfter = TimeSpan.FromDays(30), Name = "system_logs_ttl" }));
        // Common query shape: filter by level + sort by timestamp desc.
        await systemLogs.Indexes.CreateOneAsync(new CreateIndexModel<MongoDB.Bson.BsonDocument>(
            Builders<MongoDB.Bson.BsonDocument>.IndexKeys.Ascending("level").Descending("timestamp"),
            new CreateIndexOptions { Name = "system_logs_level_ts" }));
        await systemLogs.Indexes.CreateOneAsync(new CreateIndexModel<MongoDB.Bson.BsonDocument>(
            Builders<MongoDB.Bson.BsonDocument>.IndexKeys.Ascending("category").Descending("timestamp"),
            new CreateIndexOptions { Name = "system_logs_category_ts" }));
    }
}
