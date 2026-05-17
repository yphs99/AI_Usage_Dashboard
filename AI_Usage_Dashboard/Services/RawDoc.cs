using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AI_Usage_Dashboard.Services;

// JsonElement → BsonDocument conversion plus convenience upsert builders.
// Keeps raw API payloads queryable via Aggregate without losing any field.
internal static class RawDoc
{
    public static BsonDocument FromJson(JsonElement element)
    {
        var json = element.GetRawText();
        return BsonDocument.Parse(json);
    }

    public static string? ReadString(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var part in path)
        {
            if (current.ValueKind != JsonValueKind.Object) return null;
            if (!current.TryGetProperty(part, out current)) return null;
        }
        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => current.ToString()
        };
    }

    public static long ReadLong(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var part in path)
        {
            if (current.ValueKind != JsonValueKind.Object) return 0;
            if (!current.TryGetProperty(part, out current)) return 0;
        }
        return current.ValueKind switch
        {
            JsonValueKind.Number => current.TryGetInt64(out var l) ? l : (long)current.GetDouble(),
            JsonValueKind.String => long.TryParse(current.GetString(), out var s) ? s : 0,
            _ => 0
        };
    }

    public static decimal ReadDecimal(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var part in path)
        {
            if (current.ValueKind != JsonValueKind.Object) return 0m;
            if (!current.TryGetProperty(part, out current)) return 0m;
        }
        return current.ValueKind switch
        {
            JsonValueKind.Number => current.TryGetDecimal(out var d) ? d : (decimal)current.GetDouble(),
            JsonValueKind.String => decimal.TryParse(current.GetString(), out var s) ? s : 0m,
            _ => 0m
        };
    }

    public static UpdateOneModel<BsonDocument> ReplaceByKey(
        BsonDocument doc,
        params (string Field, BsonValue Value)[] keyParts)
    {
        var filter = Builders<BsonDocument>.Filter.And(
            keyParts.Select(p => Builders<BsonDocument>.Filter.Eq(p.Field, p.Value)));

        var update = new BsonDocument("$set", doc);
        return new UpdateOneModel<BsonDocument>(filter, update) { IsUpsert = true };
    }
}
