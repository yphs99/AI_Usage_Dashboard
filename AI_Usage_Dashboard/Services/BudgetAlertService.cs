using MongoDB.Bson;
using MongoDB.Driver;
using AI_Usage_Dashboard.Data;
using AI_Usage_Dashboard.Models;

namespace AI_Usage_Dashboard.Services;

public sealed class BudgetAlertService(MongoDbContext db)
{
    public async Task RecalculateAsync(IEnumerable<string> projectIds, DateTime monthStart, CancellationToken ct = default)
    {
        var monthEnd = monthStart.AddMonths(1);
        foreach (var pid in projectIds)
        {
            var budget = await db.Budgets.Find(Builders<Budget>.Filter.Eq(x => x.ProjectId, pid)).FirstOrDefaultAsync(ct);
            if (budget is null) continue;

            var spent = await SumCostAsync(pid, monthStart, monthEnd, ct);
            var pct   = budget.MonthlyBudget > 0 ? (double)(spent / budget.MonthlyBudget) * 100.0 : 0.0;
            var level = pct switch { >= 100 => "critical", >= 90 => "high", >= 80 => "warning", _ => "ok" };

            var prev = budget.Level;
            budget.Spent     = spent;
            budget.Pct       = Math.Round(pct, 2);
            budget.Remaining = budget.MonthlyBudget - spent;
            budget.Level     = level;
            budget.UpdatedAt = DateTime.UtcNow;

            await db.Budgets.ReplaceOneAsync(
                Builders<Budget>.Filter.Eq(x => x.ProjectId, pid), budget, new ReplaceOptions { IsUpsert = false }, ct);

            await TriggerAlertsAsync(budget, prev, ct);
        }
    }

    private async Task<decimal> SumCostAsync(string projectId, DateTime from, DateTime to, CancellationToken ct)
    {
        var pipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument
            {
                { "projectId", projectId },
                { "date", new BsonDocument { { "$gte", from }, { "$lt", to } } }
            }),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", 1 },
                { "cost", new BsonDocument("$sum", new BsonDocument("$toDecimal", new BsonDocument("$ifNull", new BsonArray { "$amount.value", 0 }))) }
            })
        };

        var first = (await db.OpenAiCostsRaw.Aggregate<BsonDocument>(pipeline, cancellationToken: ct).ToListAsync(ct))
            .FirstOrDefault();
        return first?.GetValue("cost", BsonDecimal128.Create(0m)).ToDecimal() ?? 0m;
    }

    private async Task TriggerAlertsAsync(Budget budget, string prev, CancellationToken ct)
    {
        var thresholds = new[] { (100, "critical"), (90, "high"), (80, "warning") };
        foreach (var (pct, lvl) in thresholds)
        {
            if (!IsAtOrAbove(budget.Level, lvl) || IsAtOrAbove(prev, lvl)) continue;
            var alert = new AlertEvent
            {
                Id        = ObjectId.GenerateNewId(),
                Timestamp = DateTime.UtcNow,
                ProjectId = budget.ProjectId,
                Level     = lvl,
                Message   = $"{budget.ProjectName} 已使用預算的 {budget.Pct:F0}%",
                Threshold = pct
            };
            await db.AlertEvents.InsertOneAsync(alert, null, ct);
        }
    }

    private static bool IsAtOrAbove(string level, string target) => level switch
    {
        "critical" => true,
        "high"     => target is "warning" or "high",
        "warning"  => target is "warning",
        _          => false
    };
}
