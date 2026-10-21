namespace Lab.Stubs;

public sealed record StubStats(
    int EnrichmentCalls,
    int EnrichmentMaxInFlight,
    int EnrichmentHangs,
    int RecordsCalls,
    int RecordsMaxInFlight,
    int RecordsOfferedMax,
    int RecordsRejected);

/// <summary>
/// Two simulated dependencies with deliberately different shapes:
/// <list type="bullet">
/// <item>An enrichment API that tolerates many concurrent calls but is slow (latency-bound).</item>
/// <item>A fragile system of record that processes a few requests at a time, queues a few more,
/// and answers 503 beyond that (capacity-bound).</item>
/// </list>
/// These numbers model the shapes, not any real system. Change them only before a
/// pre-registered run (see PREREGISTRATION.md).
/// </summary>
public sealed class StubState
{
    // Enrichment API (latency-bound).
    public const double EnrichmentMedianMsHealthy = 120;
    public const double EnrichmentMedianMsDegraded = 1200;
    public const double EnrichmentSigma = 0.25;          // log-normal spread around the median
    public const double EnrichmentHangRateDegraded = 0.05;
    public const int EnrichmentHangMs = 8000;           // longer than the client's timeout

    // System of record (capacity-bound).
    public const int RecordsCapacity = 6;               // requests processed at the same time
    public const int RecordsQueueLimit = 6;             // requests allowed to wait; beyond this, 503
    public const double RecordsServiceMsHealthy = 30;
    public const double RecordsServiceMsDegraded = 150;
    public const double RecordsSigma = 0.15;

    private readonly SemaphoreSlim _recordsCapacity = new(RecordsCapacity, RecordsCapacity);
    private volatile string _scenario = "healthy";

    private int _enrichmentInFlight;
    private int _enrichmentMax;
    private int _enrichmentCalls;
    private int _enrichmentHangs;

    private int _recordsInFlight;
    private int _recordsMax;
    private int _recordsOfferedMax;
    private int _recordsCalls;
    private int _recordsRejected;

    public string Scenario => _scenario;

    public bool TrySetScenario(string name)
    {
        if (name is not ("healthy" or "enrichment-degraded" or "records-degraded"))
        {
            return false;
        }

        _scenario = name;
        return true;
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _enrichmentMax, Volatile.Read(ref _enrichmentInFlight));
        Interlocked.Exchange(ref _enrichmentCalls, 0);
        Interlocked.Exchange(ref _enrichmentHangs, 0);
        Interlocked.Exchange(ref _recordsMax, Volatile.Read(ref _recordsInFlight));
        Interlocked.Exchange(ref _recordsOfferedMax, Volatile.Read(ref _recordsInFlight));
        Interlocked.Exchange(ref _recordsCalls, 0);
        Interlocked.Exchange(ref _recordsRejected, 0);
    }

    public StubStats Snapshot() => new(
        Volatile.Read(ref _enrichmentCalls),
        Volatile.Read(ref _enrichmentMax),
        Volatile.Read(ref _enrichmentHangs),
        Volatile.Read(ref _recordsCalls),
        Volatile.Read(ref _recordsMax),
        Volatile.Read(ref _recordsOfferedMax),
        Volatile.Read(ref _recordsRejected));

    public void EnterEnrichment()
    {
        Interlocked.Increment(ref _enrichmentCalls);
        UpdateMax(ref _enrichmentMax, Interlocked.Increment(ref _enrichmentInFlight));
    }

    public void ExitEnrichment() => Interlocked.Decrement(ref _enrichmentInFlight);

    public TimeSpan NextEnrichmentDelay()
    {
        if (_scenario == "enrichment-degraded")
        {
            if (Random.Shared.NextDouble() < EnrichmentHangRateDegraded)
            {
                Interlocked.Increment(ref _enrichmentHangs);
                return TimeSpan.FromMilliseconds(EnrichmentHangMs);
            }

            return LogNormal(EnrichmentMedianMsDegraded, EnrichmentSigma);
        }

        return LogNormal(EnrichmentMedianMsHealthy, EnrichmentSigma);
    }

    /// <summary>Returns false (and counts a rejection) when capacity plus queue is full.</summary>
    public bool TryEnterRecords()
    {
        Interlocked.Increment(ref _recordsCalls);
        var offered = Interlocked.Increment(ref _recordsInFlight);
        UpdateMax(ref _recordsOfferedMax, offered);
        if (offered > RecordsCapacity + RecordsQueueLimit)
        {
            Interlocked.Decrement(ref _recordsInFlight);
            Interlocked.Increment(ref _recordsRejected);
            return false;
        }

        UpdateMax(ref _recordsMax, offered);
        return true;
    }

    public void ExitRecords() => Interlocked.Decrement(ref _recordsInFlight);

    public async Task ProcessRecordAsync(CancellationToken cancellationToken)
    {
        await _recordsCapacity.WaitAsync(cancellationToken);
        try
        {
            var median = _scenario == "records-degraded" ? RecordsServiceMsDegraded : RecordsServiceMsHealthy;
            await Task.Delay(LogNormal(median, RecordsSigma), cancellationToken);
        }
        finally
        {
            _recordsCapacity.Release();
        }
    }

    private static TimeSpan LogNormal(double medianMs, double sigma)
    {
        // Box-Muller transform for a standard normal sample.
        var u1 = 1.0 - Random.Shared.NextDouble();
        var u2 = Random.Shared.NextDouble();
        var normal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return TimeSpan.FromMilliseconds(medianMs * Math.Exp(sigma * normal));
    }

    private static void UpdateMax(ref int max, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref max)))
        {
            if (Interlocked.CompareExchange(ref max, value, current) == current)
            {
                return;
            }
        }
    }
}
