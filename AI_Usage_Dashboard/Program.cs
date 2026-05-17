using AI_Usage_Dashboard.Data;
using AI_Usage_Dashboard.Services;
using AI_Usage_Dashboard.Workers;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

// ── Controllers + JSON ──────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

// ── CORS ────────────────────────────────────────────────────────────────────
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins("http://0.0.0.0:56176", "https://0.0.0.0:56175").AllowAnyHeader().AllowAnyMethod()));

// ── MongoDB ─────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<MongoDbContext>();

// ── OpenAI HTTP client ──────────────────────────────────────────────────────
builder.Services.AddHttpClient<OpenAiHttpClient>((sp, http) =>
{
    var config   = sp.GetRequiredService<IConfiguration>();
    var adminKey = config["OpenAI:AdminKey"]
        ?? throw new InvalidOperationException("OpenAI:AdminKey is not configured.");
    var baseUrl  = config["OpenAI:BaseUrl"] ?? "https://api.openai.com/v1/";

    http.BaseAddress = new Uri(baseUrl);
    http.Timeout = TimeSpan.FromMinutes(5);
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminKey);
    http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

    // Scope every admin call to a single organisation when configured. OpenAI's
    // Admin Key can span multiple orgs; without this header the Usage / Costs /
    // Users / API Keys endpoints return all of them. Setting it locks the entire
    // worker to one org's data.
    var orgId = config["OpenAI:OrganizationId"];
    if (!string.IsNullOrWhiteSpace(orgId))
        http.DefaultRequestHeaders.Add("OpenAI-Organization", orgId.Trim());
});

// ── Azure ARM client (singleton; manages its own credential refresh) ───────
builder.Services.AddSingleton<AzureArmClient>();

// ── Raw sync services ───────────────────────────────────────────────────────
builder.Services.AddScoped<OpenAiUsageRawSync>();
builder.Services.AddScoped<OpenAiCostsRawSync>();
builder.Services.AddScoped<OpenAiCatalogRawSync>();
builder.Services.AddScoped<AzureSubscriptionsRawSync>();
builder.Services.AddScoped<AzureLocationsRawSync>();
builder.Services.AddScoped<AzureAccountsRawSync>();
builder.Services.AddScoped<AzureDeploymentsRawSync>();
builder.Services.AddScoped<AzureUsagesRawSync>();
builder.Services.AddScoped<AzureMetricsRawSync>();
builder.Services.AddScoped<AzureCostRawSync>();
builder.Services.AddScoped<AzureSnapshotOrchestrator>();

// ── Read-side services ──────────────────────────────────────────────────────
builder.Services.AddScoped<NameLookupService>();
builder.Services.AddScoped<UsageReadService>();
builder.Services.AddScoped<BudgetAlertService>();
builder.Services.AddScoped<DeprecationCatalogService>();

// ── Hosted services ─────────────────────────────────────────────────────────
builder.Services.AddSingleton<ExportJobService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ExportJobService>());
builder.Services.AddHostedService<DataFetchWorker>();

// ── Mongo logger (Warning+ persisted to system_logs) ────────────────────────
builder.Logging.AddFilter<MongoLoggerProvider>(level => level >= LogLevel.Warning);
builder.Services.AddSingleton<ILoggerProvider, MongoLoggerProvider>();

// ── Swagger ─────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Ensure MongoDB indexes + seed deprecation catalog on startup
using (var scope = app.Services.CreateScope())
{
    var db          = scope.ServiceProvider.GetRequiredService<MongoDbContext>();
    var deprecation = scope.ServiceProvider.GetRequiredService<DeprecationCatalogService>();
    await db.EnsureIndexesAsync();
    await deprecation.EnsureSeedAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();

await app.RunAsync();
