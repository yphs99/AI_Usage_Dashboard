namespace AI_Usage_Dashboard.Models;

// Read-side API DTOs. None of these are stored in MongoDB.

public sealed class OverviewStats
{
    public decimal MonthlyCost        { get; set; }
    public double  MonthlyCostDelta   { get; set; }
    public long    TotalRequests      { get; set; }
    public double  TotalRequestsDelta { get; set; }
    public long    InputTokens        { get; set; }
    public long    OutputTokens       { get; set; }
    public decimal AvgDailyCost       { get; set; }
    public double  ErrorRate          { get; set; }
    public string  Currency           { get; set; } = "USD";
}

public sealed class DailyTrendPoint
{
    public string  Date         { get; set; } = string.Empty;
    public decimal Cost         { get; set; }
    public long    Requests     { get; set; }
    public long    InputTokens  { get; set; }
    public long    OutputTokens { get; set; }
}

public sealed class PaginatedResponse<T>
{
    public List<T> Data       { get; set; } = [];
    public long    Total      { get; set; }
    public int     Page       { get; set; }
    public int     PageSize   { get; set; }
    public int     TotalPages { get; set; }
}

public sealed class UsageRecordDto
{
    public string  Date         { get; set; } = string.Empty;
    public string  OrgId        { get; set; } = string.Empty;
    public string  ProjectId    { get; set; } = string.Empty;
    public string  ProjectName  { get; set; } = string.Empty;
    public string  UserId       { get; set; } = string.Empty;
    public string  UserName     { get; set; } = string.Empty;
    public string  ApiKeyId     { get; set; } = string.Empty;
    public string  ApiKeyName   { get; set; } = string.Empty;
    public string  Model        { get; set; } = string.Empty;
    public string  Capability   { get; set; } = string.Empty;
    public long    InputTokens  { get; set; }
    public long    OutputTokens { get; set; }
    public long    Requests     { get; set; }
    public decimal CostUsd      { get; set; }
    public string  ServiceTier  { get; set; } = string.Empty;
    public string  Source       { get; set; } = string.Empty;
}

public sealed class CostBreakdownItem
{
    public string  Label        { get; set; } = string.Empty;
    public decimal CostUsd      { get; set; }
    public double  Percentage   { get; set; }
    public long    Requests     { get; set; }
    public long    InputTokens  { get; set; }
    public long    OutputTokens { get; set; }
}

public sealed class CostBreakdownResponse
{
    public List<CostBreakdownItem> Items             { get; set; } = [];
    public decimal                 TotalCostUsd      { get; set; }
    public long                    TotalRequests     { get; set; }
    public long                    TotalInputTokens  { get; set; }
    public long                    TotalOutputTokens { get; set; }
    /// <summary>
    /// Always "USD". OpenAI usage costs are returned in USD by the OpenAI Cost API,
    /// and Azure costs come from the Cost Management `CostUSD` aggregation which
    /// Azure pre-converts to USD via daily forex (regardless of the subscription's
    /// billing currency). Sent so the UI can label cells unambiguously.
    /// </summary>
    public string                  Currency          { get; set; } = "USD";
}

public sealed class UsageQueryParams
{
    public string? Source     { get; set; } = "all";
    public string? OrgId      { get; set; }
    public string? ProjectId  { get; set; }
    public string? Period     { get; set; }
    public string? StartDate  { get; set; }
    public string? EndDate    { get; set; }
    public string? Model      { get; set; }
    public string? Capability { get; set; }
    public string? GroupBy    { get; set; }
    public int     Page       { get; set; } = 1;
    public int     PageSize   { get; set; } = 15;
    public string? SortBy     { get; set; }
    public string  SortDir    { get; set; } = "desc";
    public string? Search     { get; set; }
}

public sealed class ExportRequest
{
    public string             Type    { get; set; } = "usage";
    public UsageQueryParams?  Filters { get; set; }
}

public sealed class ExportJobStatusResponse
{
    public string    JobId        { get; set; } = string.Empty;
    public string    Status       { get; set; } = string.Empty;
    public string?   DownloadUrl  { get; set; }
    public string?   ErrorMessage { get; set; }
    public DateTime  CreatedAt    { get; set; }
    public DateTime? CompletedAt  { get; set; }
}

public sealed class BudgetUpdateRequest
{
    public decimal MonthlyBudget { get; set; }
}

public sealed class BudgetListResponse
{
    public List<Budget>  Items   { get; set; } = [];
    public BudgetSummary Summary { get; set; } = new();
}

public sealed class BudgetSummary
{
    public int Critical { get; set; }
    public int Warning  { get; set; }
    public int Ok       { get; set; }
}

public sealed class DeprecatedModelInfo
{
    public string       ModelName         { get; set; } = string.Empty;
    public string       SubstituteModel   { get; set; } = string.Empty;
    public string       ShutdownDate      { get; set; } = string.Empty;
    public int          DaysUntilShutdown { get; set; }
    public string       Urgency           { get; set; } = "upcoming";
    public long         TotalRequests     { get; set; }
    public decimal      TotalCostUsd      { get; set; }
    public List<string> OpenAiProjects    { get; set; } = [];
    public List<string> AzureProjects     { get; set; } = [];
    public string       LastSeenDate      { get; set; } = string.Empty;
}

public sealed class DeprecationSummary
{
    public int                       TotalDeprecated { get; set; }
    public int                       Expired         { get; set; }
    public int                       Critical        { get; set; }
    public int                       Warning         { get; set; }
    public int                       Upcoming        { get; set; }
    public List<DeprecatedModelInfo> Models          { get; set; } = [];
}

public sealed class ProjectDto
{
    public string ProjectId   { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
}

public sealed class OrgDto
{
    public string             OrgId    { get; set; } = string.Empty;
    public string             OrgName  { get; set; } = string.Empty;
    public List<ProjectDto>   Projects { get; set; } = [];
}

public sealed class StackedTrendResponse
{
    public decimal                  TotalSpend    { get; set; }
    public decimal                  AvgDailySpend { get; set; }
    public List<StackedTrendSeries> Series        { get; set; } = [];
    public List<StackedTrendDay>    Days          { get; set; } = [];
    public string                   Currency      { get; set; } = "USD";
}

public sealed class StackedTrendSeries
{
    public string  Key          { get; set; } = string.Empty;
    public string  Label        { get; set; } = string.Empty;
    public decimal TotalCostUsd { get; set; }
}

public sealed class StackedTrendDay
{
    public string                       Date         { get; set; } = string.Empty;
    public decimal                      TotalCostUsd { get; set; }
    public Dictionary<string, decimal>  BySeries     { get; set; } = [];
}
