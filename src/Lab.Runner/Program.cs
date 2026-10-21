using System.Collections.Concurrent;
using System.Diagnostics;
using Lab.Runner;

LabOptions options;
try
{
    options = LabOptions.Parse(args);
}
catch (Exception ex) when (ex is ArgumentException or FormatException)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

var errors = options.Validate();
if (errors.Count > 0)
{
    foreach (var error in errors)
    {
        Console.Error.WriteLine(error);
    }
    return 2;
}

using var http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
{
    BaseAddress = options.StubsBaseAddress,
    // Every call gets its own timeout inside the strategy's gate.
    Timeout = Timeout.InfiniteTimeSpan,
};

await StubControl.PrepareAsync(http, options.Scenario, CancellationToken.None);

using var strategy = ConcurrencyStrategy.Create(options);
var latencies = new LatencyRecorder();
var processor = new ItemProcessor(
    new HttpEnrichmentClient(http),
    new HttpRecordStore(http),
    strategy,
    options,
    latencies);

var statuses = new ConcurrentDictionary<ItemStatus, int>();
var items = Enumerable.Range(1, options.Items).ToArray();

var stopwatch = Stopwatch.StartNew();
await strategy.ForEachItemAsync(
    items,
    async (itemId, cancellationToken) =>
    {
        var status = await processor.ProcessAsync(itemId, cancellationToken);
        statuses.AddOrUpdate(status, 1, (_, count) => count + 1);
    },
    CancellationToken.None);
stopwatch.Stop();

var stubs = await StubControl.GetStatsAsync(http, CancellationToken.None);

var result = new RunResult(
    options.RunId,
    DateTimeOffset.UtcNow,
    strategy.Name,
    LabOptions.ScenarioName(options.Scenario),
    Environment.ProcessorCount,
    options,
    stopwatch.Elapsed.TotalSeconds,
    statuses.GetValueOrDefault(ItemStatus.Complete),
    statuses.GetValueOrDefault(ItemStatus.Incomplete),
    statuses.GetValueOrDefault(ItemStatus.WriteRejected),
    statuses.GetValueOrDefault(ItemStatus.Failed),
    latencies.EnrichmentPercentile(50),
    latencies.EnrichmentPercentile(95),
    latencies.RecordsPercentile(50),
    latencies.RecordsPercentile(95),
    stubs);

Console.WriteLine(result.ToSummary());
if (options.OutputCsv is not null)
{
    RunResult.AppendCsv(options.OutputCsv, result);
}

return 0;
