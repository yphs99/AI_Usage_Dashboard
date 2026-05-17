using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;

namespace AI_Usage_Dashboard.Models;

// Only entities not derived from upstream APIs live here.
// Worker writes raw API responses straight into BsonDocument collections —
// no typed POCO for those (architecture principle ①).

public sealed class Budget
{
    [BsonId]
    public string ProjectId { get; set; } = string.Empty;

    [BsonElement("projectName")]
    public string ProjectName { get; set; } = string.Empty;

    [BsonElement("monthlyBudget")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal MonthlyBudget { get; set; }

    [BsonElement("spent")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Spent { get; set; }

    [BsonElement("pct")]
    public double Pct { get; set; }

    [BsonElement("remaining")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Remaining { get; set; }

    [BsonElement("level")]
    public string Level { get; set; } = "ok";

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

public sealed class AlertEvent
{
    [BsonId]
    [JsonIgnore]
    public ObjectId Id { get; set; }

    [BsonElement("timestamp")]
    public DateTime Timestamp { get; set; }

    [BsonElement("projectId")]
    public string ProjectId { get; set; } = string.Empty;

    [BsonElement("level")]
    public string Level { get; set; } = string.Empty;

    [BsonElement("message")]
    public string Message { get; set; } = string.Empty;

    [BsonElement("threshold")]
    public int Threshold { get; set; }
}

public sealed class ExportJob
{
    [BsonId]
    public string JobId { get; set; } = string.Empty;

    [BsonElement("status")]
    public string Status { get; set; } = "pending";

    [BsonElement("type")]
    public string Type { get; set; } = string.Empty;

    [BsonElement("filtersJson")]
    public string FiltersJson { get; set; } = string.Empty;

    [BsonElement("downloadUrl")]
    public string? DownloadUrl { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("completedAt")]
    public DateTime? CompletedAt { get; set; }

    [BsonElement("errorMessage")]
    public string? ErrorMessage { get; set; }
}

public sealed class FetchCheckpoint
{
    [BsonId]
    public string Source { get; set; } = string.Empty;

    [BsonElement("lastFetchedAt")]
    public DateTime LastFetchedAt { get; set; }
}

public sealed class DeprecationCatalogEntry
{
    [BsonId]
    public string Model { get; set; } = string.Empty;

    [BsonElement("shutdownDate")]
    public string ShutdownDate { get; set; } = string.Empty;

    [BsonElement("replacementModel")]
    public string ReplacementModel { get; set; } = string.Empty;

    [BsonElement("isEnabled")]
    public bool IsEnabled { get; set; } = true;

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}
