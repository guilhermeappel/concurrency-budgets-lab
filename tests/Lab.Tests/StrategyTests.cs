using System.Collections.Concurrent;
using Lab.Runner;

namespace Lab.Tests;

public sealed class StrategyTests
{
    private static readonly TimeSpan CallDelay = TimeSpan.FromMilliseconds(15);

    private static async Task<ConcurrentDictionary<int, ItemStatus>> RunAsync(
        LabOptions options,
        IEnrichmentClient enrichment,
        IRecordStore records)
    {
        using var strategy = ConcurrencyStrategy.Create(options);
        var processor = new ItemProcessor(enrichment, records, strategy, options, new LatencyRecorder());
        var results = new ConcurrentDictionary<int, ItemStatus>();
        var items = Enumerable.Range(1, options.Items).ToArray();

        await strategy.ForEachItemAsync(
            items,
            async (itemId, cancellationToken) => results[itemId] = await processor.ProcessAsync(itemId, cancellationToken),
            CancellationToken.None);

        return results;
    }

    [Fact]
    public async Task C1_runtime_default_never_exceeds_the_processor_count()
    {
        var probe = new ConcurrencyProbe();
        var options = new LabOptions { Strategy = StrategyKind.C1, Items = 40, CallsPerItem = 1 };

        await RunAsync(options, new FakeEnrichment(probe, CallDelay), new FakeRecords(new ConcurrencyProbe(), CallDelay));

        Assert.True(probe.Max <= Environment.ProcessorCount, $"max {probe.Max} > ProcessorCount {Environment.ProcessorCount}");
    }

    [Fact]
    public async Task C2_single_limit_bounds_items_but_not_the_system_of_record()
    {
        // Longer calls so the 20 items reliably overlap at the system of record.
        var slowCall = TimeSpan.FromMilliseconds(50);
        var records = new ConcurrencyProbe();
        var options = new LabOptions { Strategy = StrategyKind.C2, Items = 60, CallsPerItem = 1, ItemParallelism = 20 };

        await RunAsync(options, new FakeEnrichment(new ConcurrencyProbe(), slowCall), new FakeRecords(records, slowCall));

        Assert.True(records.Max <= 20);
        Assert.True(records.Max > 6, $"expected the system of record to see more than 6 at once, saw {records.Max}");
    }

    [Fact]
    public async Task C3_shared_limiter_bounds_the_sum_of_both_dependencies()
    {
        var shared = new ConcurrencyProbe();
        var options = new LabOptions { Strategy = StrategyKind.C3, Items = 60, CallsPerItem = 2, ItemParallelism = 30, SharedLimit = 8 };

        await RunAsync(options, new FakeEnrichment(shared, CallDelay), new FakeRecords(shared, CallDelay));

        Assert.True(shared.Max <= 8, $"max {shared.Max} > shared limit 8");
    }

    [Fact]
    public async Task C4_per_dependency_keeps_each_dependency_inside_its_own_budget()
    {
        var enrichment = new ConcurrencyProbe();
        var records = new ConcurrencyProbe();
        var options = new LabOptions
        {
            Strategy = StrategyKind.C4,
            Items = 80,
            CallsPerItem = 2,
            ItemParallelism = 30,
            EnrichmentLimit = 20,
            RecordsLimit = 3,
        };

        await RunAsync(options, new FakeEnrichment(enrichment, CallDelay), new FakeRecords(records, CallDelay));

        Assert.True(enrichment.Max <= 20, $"enrichment max {enrichment.Max} > 20");
        Assert.True(records.Max <= 3, $"records max {records.Max} > 3");
    }

    [Fact]
    public async Task A_timed_out_part_is_written_as_incomplete_never_as_complete()
    {
        var store = new FakeRecords(new ConcurrencyProbe(), CallDelay);
        var options = new LabOptions
        {
            Strategy = StrategyKind.C4,
            Items = 6,
            CallsPerItem = 3,
            ItemParallelism = 6,
            EnrichmentLimit = 6,
            RecordsLimit = 3,
            EnrichmentTimeout = TimeSpan.FromSeconds(1), // long enough for healthy calls on a busy CI machine
        };

        var results = await RunAsync(
            options,
            new FakeEnrichment(new ConcurrencyProbe(), CallDelay, hangs: (itemId, part) => itemId == 5 && part == 1),
            store);

        Assert.Equal(ItemStatus.Incomplete, results[5]);
        Assert.False(store.Written[5]);
        Assert.All(results.Where(r => r.Key != 5), r => Assert.Equal(ItemStatus.Complete, r.Value));
    }
}
