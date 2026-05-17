using System.Collections.Concurrent;
using MongoDB.Bson;
using MongoDB.Driver;
using AI_Usage_Dashboard.Data;

namespace AI_Usage_Dashboard.Services;

// Persists Warning+ log events into MongoDB `system_logs` for offline analysis.
// Implementation: ILoggerProvider + ILogger that buffer entries on a Channel and
// flush them in batches by a single background task. We avoid Mongo writes on
// the hot path (each log call just enqueues).
//
// Schema of each `system_logs` document:
//   _id          : ObjectId
//   timestamp    : DateTime (UTC)
//   level        : "warn" | "error" | "critical"
//   category     : ILogger category (e.g. AI_Usage_Dashboard.Services.AzureCostRawSync)
//   eventId      : int
//   message      : formatted message
//   exception    : full exception ToString() (when present)
//   exceptionType: short type name when present
public sealed class MongoLoggerProvider : ILoggerProvider
{
    private readonly System.Threading.Channels.Channel<BsonDocument> _queue =
        System.Threading.Channels.Channel.CreateBounded<BsonDocument>(
            new System.Threading.Channels.BoundedChannelOptions(10_000)
            {
                FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest
            });

    private readonly ConcurrentDictionary<string, MongoLogger> _loggers = new();
    private readonly IServiceProvider _sp;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _flushTask;

    public MongoLoggerProvider(IServiceProvider sp)
    {
        _sp = sp;
        _flushTask = Task.Run(() => FlushLoopAsync(_cts.Token));
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, n => new MongoLogger(n, _queue.Writer));

    public void Dispose()
    {
        try
        {
            _cts.Cancel();
            _queue.Writer.TryComplete();
            _flushTask.Wait(TimeSpan.FromSeconds(3));
        }
        catch { /* shutdown best-effort */ }
        _cts.Dispose();
    }

    private async Task FlushLoopAsync(CancellationToken ct)
    {
        var buffer = new List<BsonDocument>(128);
        try
        {
            await foreach (var doc in _queue.Reader.ReadAllAsync(ct))
            {
                buffer.Add(doc);
                // Drain anything else already pending without blocking
                while (buffer.Count < 256 && _queue.Reader.TryRead(out var more))
                    buffer.Add(more);

                await TryFlushAsync(buffer);
                buffer.Clear();
            }
        }
        catch (OperationCanceledException) { }
        catch { /* don't crash the host on logging error */ }

        if (buffer.Count > 0)
            await TryFlushAsync(buffer);
    }

    private async Task TryFlushAsync(IReadOnlyList<BsonDocument> docs)
    {
        try
        {
            using var scope = _sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MongoDbContext>();
            var col = db.Database.GetCollection<BsonDocument>("system_logs");
            await col.InsertManyAsync(docs, new InsertManyOptions { IsOrdered = false });
        }
        catch { /* never let logging errors propagate */ }
    }
}

internal sealed class MongoLogger : ILogger
{
    private readonly string _category;
    private readonly System.Threading.Channels.ChannelWriter<BsonDocument> _writer;

    public MongoLogger(string category, System.Threading.Channels.ChannelWriter<BsonDocument> writer)
    {
        _category = category;
        _writer = writer;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var doc = new BsonDocument
        {
            ["timestamp"] = DateTime.UtcNow,
            ["level"]     = logLevel switch
            {
                LogLevel.Warning  => "warn",
                LogLevel.Error    => "error",
                LogLevel.Critical => "critical",
                _                 => logLevel.ToString().ToLowerInvariant()
            },
            ["category"]  = _category,
            ["eventId"]   = eventId.Id,
            ["message"]   = formatter(state, exception) ?? string.Empty
        };
        if (exception is not null)
        {
            doc["exception"]     = exception.ToString();
            doc["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name;
        }

        // Best-effort enqueue; if the bounded channel is full it drops oldest.
        _writer.TryWrite(doc);
    }
}
