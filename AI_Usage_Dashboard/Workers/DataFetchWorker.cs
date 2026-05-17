using MongoDB.Driver;
using AI_Usage_Dashboard.Data;
using AI_Usage_Dashboard.Models;
using AI_Usage_Dashboard.Services;

namespace AI_Usage_Dashboard.Workers;

// Pure raw-sync orchestrator. Calls each *RawSync service in turn; never touches
// the response shape. Architecture principle ①: this worker has no business logic.
public sealed class DataFetchWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<DataFetchWorker> logger) : BackgroundService
{
    private readonly int _intervalMinutes      = config.GetValue("FetchWorker:IntervalMinutes",   30);
    private readonly int _historyDays          = config.GetValue("FetchWorker:HistoryDays",       90);
    private readonly int _catalogIntervalMins  = config.GetValue("CatalogSync:IntervalMinutes",  180);
    private readonly int _azureCostIntervalMins= config.GetValue("AzureCost:IntervalMinutes",     30);
    private readonly int _azureSnapshotMins    = config.GetValue("AzureSnapshot:IntervalMinutes", 60);
    private readonly int _azureSnapshotWindow  = config.GetValue("AzureSnapshot:UsageWindowDays", 30);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await RunCycleAsync(ct);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_intervalMinutes));
        while (await timer.WaitForNextTickAsync(ct))
            await RunCycleAsync(ct);
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MongoDbContext>();

        await TryAsync("openai_usage",   () => SyncOpenAiUsageAsync(sp, db, ct), ct);
        await TryAsync("openai_costs",   () => SyncOpenAiCostsAsync(sp, db, ct), ct);
        await TryAsync("openai_catalog", () => SyncOpenAiCatalogAsync(sp, db, ct), ct, _catalogIntervalMins);
        await TryAsync("azure_snapshot", () => SyncAzureSnapshotAsync(sp, db, ct), ct, _azureSnapshotMins);
    }

    private async Task SyncOpenAiUsageAsync(IServiceProvider sp, MongoDbContext db, CancellationToken ct)
    {
        var svc = sp.GetRequiredService<OpenAiUsageRawSync>();
        var (start, end) = ResolveWindowAsync(db, "openai_usage").Result;
        await svc.SyncAsync(start, end, ct);
        await SetCheckpointAsync(db, "openai_usage", end);
    }

    private async Task SyncOpenAiCostsAsync(IServiceProvider sp, MongoDbContext db, CancellationToken ct)
    {
        var svc = sp.GetRequiredService<OpenAiCostsRawSync>();
        var (start, end) = ResolveWindowAsync(db, "openai_costs").Result;
        await svc.SyncAsync(start, end, ct);
        await SetCheckpointAsync(db, "openai_costs", end);
    }

    private async Task SyncOpenAiCatalogAsync(IServiceProvider sp, MongoDbContext db, CancellationToken ct)
    {
        var svc = sp.GetRequiredService<OpenAiCatalogRawSync>();
        await svc.SyncAsync(ct);
        await SetCheckpointAsync(db, "openai_catalog", DateTimeOffset.UtcNow);
    }

    private async Task SyncAzureSnapshotAsync(IServiceProvider sp, MongoDbContext db, CancellationToken ct)
    {
        var svc = sp.GetRequiredService<AzureSnapshotOrchestrator>();
        var end = DateTimeOffset.UtcNow;
        // First-run (no checkpoint): backfill UsageWindowDays (default 90).
        // Subsequent runs: re-fetch from checkpoint − 2 days for safety overlap.
        // This mirrors the openai_usage / openai_costs behaviour so all sources
        // share the same "first-time deep history, then incremental" semantic.
        var checkpoint = await GetCheckpointAsync(db, "azure_snapshot");
        var start = checkpoint?.AddDays(-2) ?? end.AddDays(-Math.Max(1, _azureSnapshotWindow));
        await svc.SyncAsync(start, end, ct);
        await SetCheckpointAsync(db, "azure_snapshot", end);
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    private async Task TryAsync(string label, Func<Task> action, CancellationToken ct, int? minIntervalMinutes = null)
    {
        if (minIntervalMinutes.HasValue)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MongoDbContext>();
            var checkpoint = await GetCheckpointAsync(db, label);
            if (checkpoint.HasValue && (DateTime.UtcNow - checkpoint.Value).TotalMinutes < minIntervalMinutes.Value)
                return;
        }
        try { await action(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { logger.LogWarning(ex, "Sync {Label} failed", label); }
    }

    private async Task<(DateTimeOffset Start, DateTimeOffset End)> ResolveWindowAsync(MongoDbContext db, string source)
    {
        var checkpoint = await GetCheckpointAsync(db, source);
        var end   = DateTimeOffset.UtcNow;
        var start = checkpoint?.AddDays(-2) ?? DateTimeOffset.UtcNow.AddDays(-_historyDays);
        return (start, end);
    }

    private static async Task<DateTime?> GetCheckpointAsync(MongoDbContext db, string source)
    {
        var doc = await db.FetchCheckpoints
            .Find(Builders<FetchCheckpoint>.Filter.Eq(x => x.Source, source))
            .FirstOrDefaultAsync();
        return doc?.LastFetchedAt;
    }

    private static Task SetCheckpointAsync(MongoDbContext db, string source, DateTimeOffset at)
    {
        var doc = new FetchCheckpoint { Source = source, LastFetchedAt = at.UtcDateTime };
        return db.FetchCheckpoints.ReplaceOneAsync(
            Builders<FetchCheckpoint>.Filter.Eq(x => x.Source, source),
            doc, new ReplaceOptions { IsUpsert = true });
    }
}
