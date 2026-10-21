using System.Collections.Concurrent;
using Lab.Runner;

namespace Lab.Tests;

/// <summary>Counts how many calls are inside a dependency at the same time.</summary>
public sealed class ConcurrencyProbe
{
    private int _inFlight;
    private int _max;

    public int Max => Volatile.Read(ref _max);

    public async Task<T> EnterAsync<T>(TimeSpan delay, T value, CancellationToken cancellationToken)
    {
        var now = Interlocked.Increment(ref _inFlight);
        int current;
        while (now > (current = Volatile.Read(ref _max)))
        {
            if (Interlocked.CompareExchange(ref _max, now, current) == current)
            {
                break;
            }
        }

        try
        {
            await Task.Delay(delay, cancellationToken);
            return value;
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }
}

public sealed class FakeEnrichment(ConcurrencyProbe probe, TimeSpan delay, Func<int, int, bool>? hangs = null) : IEnrichmentClient
{
    public Task<int> GetAsync(int itemId, int part, CancellationToken cancellationToken)
    {
        var actualDelay = hangs?.Invoke(itemId, part) == true ? Timeout.InfiniteTimeSpan : delay;
        return probe.EnterAsync(actualDelay, itemId * 31 + part, cancellationToken);
    }
}

public sealed class FakeRecords(ConcurrencyProbe probe, TimeSpan delay) : IRecordStore
{
    public ConcurrentDictionary<int, bool> Written { get; } = new();

    public async Task<WriteOutcome> WriteAsync(int itemId, bool complete, CancellationToken cancellationToken)
    {
        var outcome = await probe.EnterAsync(delay, WriteOutcome.Accepted, cancellationToken);
        Written[itemId] = complete;
        return outcome;
    }
}
