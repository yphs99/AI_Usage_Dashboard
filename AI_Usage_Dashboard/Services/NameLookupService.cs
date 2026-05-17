using MongoDB.Bson;
using MongoDB.Driver;
using AI_Usage_Dashboard.Data;

namespace AI_Usage_Dashboard.Services;

// Read-time projectId/userId/apiKeyId → name lookup from raw catalog collections.
// Cached for 1 minute; raw collections are the only source of truth (architecture
// principle ②: dropdowns and name resolution come straight from the synced raw data).
public sealed class NameLookupService(MongoDbContext db)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(1);
    private static readonly object _lock = new();

    private static Dictionary<string, string>? _projects;
    private static Dictionary<string, string>? _users;
    private static Dictionary<string, string>? _apiKeys;
    private static DateTime _projectsExp;
    private static DateTime _usersExp;
    private static DateTime _apiKeysExp;

    public async Task<Dictionary<string, string>> GetProjectNamesAsync(CancellationToken ct = default)
    {
        var cached = TryRead(_projects, _projectsExp);
        if (cached is not null) return cached;

        var orgs = await db.OpenAiOrgsRaw.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync(ct);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var org in orgs)
        {
            if (!org.TryGetValue("projects", out var projEl)) continue;
            if (projEl.BsonType != BsonType.Document) continue;
            if (!projEl.AsBsonDocument.TryGetValue("data", out var dataEl) || dataEl.BsonType != BsonType.Array) continue;

            foreach (var p in dataEl.AsBsonArray)
            {
                if (p.BsonType != BsonType.Document) continue;
                var pd = p.AsBsonDocument;
                var id    = pd.GetValue("id", "").AsString;
                var title = pd.GetValue("title", id).AsString;
                if (!string.IsNullOrWhiteSpace(id))
                    map[id] = string.IsNullOrWhiteSpace(title) ? id : title;
            }
        }
        Cache(ref _projects, ref _projectsExp, map);
        return map;
    }

    public async Task<Dictionary<string, string>> GetUserNamesAsync(CancellationToken ct = default)
    {
        var cached = TryRead(_users, _usersExp);
        if (cached is not null) return cached;

        var docs = await db.OpenAiUsersRaw.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync(ct);
        var map = docs.ToDictionary(
            d => d.GetValue("_id", BsonString.Empty).AsString,
            d => d.GetValue("name", d.GetValue("_id", BsonString.Empty)).AsString,
            StringComparer.OrdinalIgnoreCase);
        Cache(ref _users, ref _usersExp, map);
        return map;
    }

    public async Task<Dictionary<string, string>> GetApiKeyNamesAsync(CancellationToken ct = default)
    {
        var cached = TryRead(_apiKeys, _apiKeysExp);
        if (cached is not null) return cached;

        var docs = await db.OpenAiApiKeysRaw.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync(ct);
        var map = docs.ToDictionary(
            d => d.GetValue("_id", BsonString.Empty).AsString,
            d => d.GetValue("name", d.GetValue("_id", BsonString.Empty)).AsString,
            StringComparer.OrdinalIgnoreCase);
        Cache(ref _apiKeys, ref _apiKeysExp, map);
        return map;
    }

    public async Task<Dictionary<string, string>> GetSubscriptionNamesAsync(CancellationToken ct = default)
    {
        var docs = await db.AzureSubscriptionsRaw.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync(ct);
        return docs.ToDictionary(
            d => d.GetValue("subscriptionId", BsonString.Empty).AsString,
            d => d.GetValue("displayName",     d.GetValue("subscriptionId", BsonString.Empty)).AsString,
            StringComparer.Ordinal);
    }

    private static Dictionary<string, string>? TryRead(Dictionary<string, string>? cache, DateTime expiresAt)
    {
        lock (_lock) return (cache is not null && DateTime.UtcNow < expiresAt) ? cache : null;
    }

    private static void Cache(ref Dictionary<string, string>? slot, ref DateTime exp, Dictionary<string, string> v)
    {
        lock (_lock) { slot = v; exp = DateTime.UtcNow + CacheTtl; }
    }
}
