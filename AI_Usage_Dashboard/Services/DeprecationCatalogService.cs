using System.Text.RegularExpressions;
using MongoDB.Driver;
using AI_Usage_Dashboard.Data;
using AI_Usage_Dashboard.Models;

namespace AI_Usage_Dashboard.Services;

// Deprecation catalog is now 100% DB-backed. The static C# registry that used to
// shadow this collection is gone (architecture principle ②).
public sealed class DeprecationCatalogService(MongoDbContext db, ILogger<DeprecationCatalogService> logger)
{
    public sealed record Snapshot(IReadOnlyDictionary<string, DeprecationCatalogEntry> Exact);

    public async Task EnsureSeedAsync(CancellationToken ct = default)
    {
        var any = await db.DeprecationCatalog.Find(Builders<DeprecationCatalogEntry>.Filter.Empty).AnyAsync(ct);
        if (any) return;
        await SeedDefaultsAsync(ct);
    }

    // Wipe + reseed the catalog with the canonical defaults. Snapshot variants
    // (e.g. -YYYY-MM-DD, -MMDD) are intentionally omitted because Lookup() strips
    // them at query time; only base names and non-date variants (-completions,
    // -latest, -preview without a date prefix) need to live here.
    public async Task<int> RebuildAsync(CancellationToken ct = default)
    {
        await db.DeprecationCatalog.DeleteManyAsync(Builders<DeprecationCatalogEntry>.Filter.Empty, ct);
        return await SeedDefaultsAsync(ct);
    }

    private async Task<int> SeedDefaultsAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var seeds = DefaultEntries.Select(e => new DeprecationCatalogEntry
        {
            Model            = e.Model,
            ShutdownDate     = e.ShutdownDate,
            ReplacementModel = e.ReplacementModel,
            IsEnabled        = true,
            UpdatedAt        = now,
        }).ToArray();
        await db.DeprecationCatalog.InsertManyAsync(seeds, cancellationToken: ct);
        logger.LogInformation("Seeded deprecation catalog with {Count} entries.", seeds.Length);
        return seeds.Length;
    }

    // Conflict-resolution rule: when the same raw model could resolve to two
    // different shutdown dates (e.g. an explicit snapshot row vs. its base
    // family row reached via StripSnapshotSuffix), the EARLIER date always wins.
    // Lookup() achieves this naturally because step 1 (exact match) is checked
    // before step 2 (strip-fallback match), so to make an earlier date win:
    //   • If it's a base name (e.g. gpt-4o-audio-preview), UPDATE the base row
    //     so every snapshot stripped to it picks up the earlier date.
    //   • If it's a snapshot variant whose stripped base has a LATER date
    //     (e.g. gpt-4-0314 strips to gpt-4 = 2026-10-23, but the 0314 snapshot
    //     itself retires 2026-03-26), INSERT an explicit row for the snapshot
    //     so the exact-match step short-circuits before strip is tried.
    private static readonly (string Model, string ShutdownDate, string ReplacementModel)[] DefaultEntries =
    [
        // ── 2026-03-26 — gpt-4 family snapshots (explicit rows override the
        // stripped-to-`gpt-4` fallback that points at 2026-10-23) ───────────
        ("gpt-4-0314",                           "2026-03-26", "gpt-5 or gpt-4.1*"),
        ("gpt-4-0125-preview",                   "2026-03-26", "gpt-5 or gpt-4.1*"),
        ("gpt-4-1106-preview",                   "2026-03-26", "gpt-5 or gpt-4.1*"),
        ("gpt-4-turbo-preview",                  "2026-03-26", "gpt-5 or gpt-4.1*"),
        ("gpt-4-turbo-preview-completions",      "2026-03-26", "gpt-5 or gpt-4.1*"),

        // ── 2026-05-07 ────────────────────────────────────────────────────────
        ("gpt-4o-audio-preview",                 "2026-05-07", "gpt-audio-1.5"),
        ("gpt-4o-mini-audio-preview",            "2026-05-07", "gpt-audio-mini"),
        ("gpt-4o-mini-realtime-preview",         "2026-05-07", "gpt-realtime-mini"),
        ("gpt-4o-realtime-preview",              "2026-05-07", "gpt-realtime-1.5"),

        // ── 2026-05-12 ────────────────────────────────────────────────────────
        ("dall-e-2",                             "2026-05-12", "gpt-image-1 or gpt-image-1-mini"),
        ("dall-e-3",                             "2026-05-12", "gpt-image-1 or gpt-image-1-mini"),

        // ── 2026-07-23 ────────────────────────────────────────────────────────
        ("computer-use-preview",                 "2026-07-23", "5.4-mini"),
        ("gpt-4o-mini-search-preview",           "2026-07-23", "4.1-mini"),
        ("gpt-4o-mini-tts",                      "2026-07-23", "gpt-realtime"),
        ("gpt-4o-search-preview",                "2026-07-23", "gpt-4.1-mini"),
        ("gpt-5-chat-latest",                    "2026-07-23", "gpt-5.3-chat-latest"),
        ("gpt-5-codex",                          "2026-07-23", "gpt-5.4"),
        ("gpt-5.1-chat-latest",                  "2026-07-23", "gpt-5.3-chat-latest"),
        ("gpt-5.1-codex",                        "2026-07-23", "gpt-5"),
        ("gpt-5.1-codex-max",                    "2026-07-23", "gpt-5.4"),
        ("gpt-5.1-codex-mini",                   "2026-07-23", "gpt-5.4-mini"),
        ("gpt-5.2-codex",                        "2026-07-23", "gpt-5.4"),
        ("gpt-audio-mini",                       "2026-07-23", "gpt-audio"),
        ("gpt-realtime-mini",                    "2026-07-23", "gpt-realtime-mini"),
        ("o3-deep-research",                     "2026-07-23", "5.4-Pro"),
        ("o4-mini-deep-research",                "2026-07-23", "5.4-Pro"),

        // ── 2026-09-28 ────────────────────────────────────────────────────────
        ("babbage-002",                          "2026-09-28", "gpt-5.4-mini or gpt-5-mini"),
        ("davinci-002",                          "2026-09-28", "gpt-5.4-mini or gpt-5-mini"),
        ("gpt-3.5-turbo-1106",                   "2026-09-28", "gpt-5.4-mini or gpt-5-mini"),
        ("gpt-3.5-turbo-instruct",               "2026-09-28", "gpt-5.4-mini or gpt-5-mini"),

        // ── 2026-10-23 ────────────────────────────────────────────────────────
        ("gpt-3.5-turbo",                        "2026-10-23", "gpt-4.1-mini"),
        ("gpt-3.5-turbo-completions",            "2026-10-23", "gpt-4.1-mini"),
        ("gpt-4",                                "2026-10-23", "gpt-4.1"),
        ("gpt-4-completions",                    "2026-10-23", "gpt-4.1"),
        ("gpt-4-0613-completions",               "2026-10-23", "gpt-4.1"),
        ("gpt-4-turbo",                          "2026-10-23", "gpt-4.1"),
        ("gpt-4-turbo-completions",              "2026-10-23", "gpt-4.1"),
        ("gpt-4.1-nano",                         "2026-10-23", "gpt-5-nano"),
        ("gpt-4o",                               "2026-10-23", "gpt-4.1"),
        ("gpt-image-1",                          "2026-10-23", "gpt-image-1.5"),
        ("o1",                                   "2026-10-23", "o3"),
        ("o1-pro",                               "2026-10-23", "5.4-Pro"),
        ("o3-mini",                              "2026-10-23", "o3"),
        ("o4-mini",                              "2026-10-23", "gpt-5-mini"),
        ("ft-o4-mini",                           "2026-10-23", "gpt-5-mini"),
        ("ft-gpt-3.5-turbo",                     "2026-10-23", "gpt-4.1-mini"),
        ("ft-gpt-4",                             "2026-10-23", "gpt-4.1"),
        ("ft-gpt-4.1-nano",                      "2026-10-23", "gpt-5-nano"),
        ("ft-babbage-002",                       "2026-10-23", "gpt-5-mini"),
        ("ft-davinci-002",                       "2026-10-23", "gpt-5-mini"),
    ];

    public async Task<Snapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var docs = await db.DeprecationCatalog.Find(Builders<DeprecationCatalogEntry>.Filter.Eq(x => x.IsEnabled, true)).ToListAsync(ct);
        var map = docs.ToDictionary(x => x.Model.Trim(), x => x, StringComparer.OrdinalIgnoreCase);
        return new Snapshot(map);
    }

    public static (bool IsDeprecated, string ShutdownDate, string ReplacementModel) Lookup(string? rawModel, Snapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(rawModel)) return (false, string.Empty, string.Empty);
        var raw = rawModel.Trim();
        if (snapshot.Exact.TryGetValue(raw, out var hit))
            return (true, hit.ShutdownDate, hit.ReplacementModel);

        // Fallback: strip an OpenAI-style snapshot suffix at the END only, then retry.
        // End-anchored ensures gpt-4o-2024-05-13 → gpt-4o (never gpt-4).
        var stripped = StripSnapshotSuffix(raw);
        if (stripped != raw && snapshot.Exact.TryGetValue(stripped, out hit))
            return (true, hit.ShutdownDate, hit.ReplacementModel);

        return (false, string.Empty, string.Empty);
    }

    // Recognises OpenAI snapshot suffixes attached to a base model name:
    //   -YYYY-MM-DD              e.g. gpt-4-turbo-2024-04-09, gpt-4o-2024-05-13
    //   -MMDD                    e.g. gpt-4-0613, gpt-3.5-turbo-0125, gpt-4-1106
    //   either, optionally followed by -preview (e.g. gpt-4-1106-preview)
    private static readonly Regex SnapshotSuffix =
        new(@"-(?:\d{4}-\d{2}-\d{2}|\d{4})(?:-preview)?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string StripSnapshotSuffix(string model) => SnapshotSuffix.Replace(model, "");
}
