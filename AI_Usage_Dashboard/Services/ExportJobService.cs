using System.Text.Json;
using System.Threading.Channels;
using MongoDB.Driver;
using AI_Usage_Dashboard.Data;
using AI_Usage_Dashboard.Models;
using AI_Usage_Dashboard.Utils;

namespace AI_Usage_Dashboard.Services;

public sealed class ExportJobService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MongoDbContext _db;
    private readonly string _exportDir;
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>();

    public ExportJobService(IServiceScopeFactory scopeFactory, MongoDbContext db, IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _db        = db;
        _exportDir = config["Export:Directory"] ?? "wwwroot/exports";
        Directory.CreateDirectory(_exportDir);
    }

    public async Task<string> CreateJobAsync(ExportRequest req)
    {
        var job = new ExportJob
        {
            JobId       = Guid.NewGuid().ToString("N"),
            Status      = "pending",
            Type        = req.Type,
            FiltersJson = JsonSerializer.Serialize(req.Filters),
            CreatedAt   = DateTime.UtcNow
        };
        await _db.ExportJobs.InsertOneAsync(job);
        await _queue.Writer.WriteAsync(job.JobId);
        return job.JobId;
    }

    public Task<ExportJob?> GetJobAsync(string jobId) =>
        _db.ExportJobs.Find(Builders<ExportJob>.Filter.Eq(x => x.JobId, jobId)).FirstOrDefaultAsync()!;

    public Task StartAsync(CancellationToken ct) { _ = ProcessQueueAsync(ct); return Task.CompletedTask; }
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        await foreach (var jobId in _queue.Reader.ReadAllAsync(ct))
        {
            await SetStatusAsync(jobId, "preparing");
            try
            {
                var job = await GetJobAsync(jobId);
                if (job is null) continue;

                var filters = JsonSerializer.Deserialize<UsageQueryParams>(job.FiltersJson) ?? new();
                var csvPath = Path.Combine(_exportDir, $"{jobId}.csv");

                using var scope = _scopeFactory.CreateScope();
                var read = scope.ServiceProvider.GetRequiredService<UsageReadService>();

                var (sd, ed) = DateRangeHelper.ResolvePeriod(filters.Period, filters.StartDate, filters.EndDate);
                var rows = new List<UsageRecordDto>();
                rows.AddRange(await read.AggregateOpenAiUsageAsync(filters.OrgId, filters.ProjectId, filters.Model, filters.Capability, sd, ed, null, ct));
                rows.AddRange(await read.AggregateAzureUsageAsync(filters.OrgId, filters.ProjectId, filters.Model, sd, ed, ct));
                rows = rows.OrderByDescending(x => x.Date).ToList();

                if (job.Type == "cost")
                    await CsvSerializer.WriteFileAsync(csvPath,
                        ["Date", "Project", "Model", "Capability", "Cost(USD)"],
                        rows.Select(r => new[] { r.Date, r.ProjectName, r.Model, r.Capability, r.CostUsd.ToString("F6") }));
                else
                    await CsvSerializer.WriteFileAsync(csvPath,
                        ["Date", "Project", "User", "ApiKey", "Model", "Capability", "InputTokens", "OutputTokens", "Requests", "Cost(USD)"],
                        rows.Select(r => new[]
                        {
                            r.Date, r.ProjectName, r.UserName, r.ApiKeyName, r.Model, r.Capability,
                            r.InputTokens.ToString(), r.OutputTokens.ToString(), r.Requests.ToString(), r.CostUsd.ToString("F6")
                        }));

                var update = Builders<ExportJob>.Update
                    .Set(x => x.Status, "ready")
                    .Set(x => x.DownloadUrl, $"/exports/{jobId}.csv")
                    .Set(x => x.CompletedAt, DateTime.UtcNow);
                await _db.ExportJobs.UpdateOneAsync(Builders<ExportJob>.Filter.Eq(x => x.JobId, jobId), update, null, ct);
            }
            catch (Exception ex)
            {
                var update = Builders<ExportJob>.Update
                    .Set(x => x.Status, "failed")
                    .Set(x => x.ErrorMessage, ex.Message)
                    .Set(x => x.CompletedAt, DateTime.UtcNow);
                await _db.ExportJobs.UpdateOneAsync(Builders<ExportJob>.Filter.Eq(x => x.JobId, jobId), update);
            }
        }
    }

    private async Task SetStatusAsync(string jobId, string status)
    {
        var update = Builders<ExportJob>.Update.Set(x => x.Status, status);
        await _db.ExportJobs.UpdateOneAsync(Builders<ExportJob>.Filter.Eq(x => x.JobId, jobId), update);
    }
}
