namespace Lab.Runner;

/// <summary>
/// The only thing that changes between C1, C2, C3 and C4: how many items run at
/// once, and which gate (if any) each dependency call has to pass.
/// </summary>
public abstract class ConcurrencyStrategy : IDisposable
{
    public abstract string Name { get; }

    public abstract Task ForEachItemAsync<T>(
        IReadOnlyList<T> items,
        Func<T, CancellationToken, ValueTask> body,
        CancellationToken cancellationToken);

    public virtual Task<TResult> CallEnrichmentAsync<TResult>(
        Func<CancellationToken, Task<TResult>> call,
        CancellationToken cancellationToken) => call(cancellationToken);

    public virtual Task<TResult> CallRecordsAsync<TResult>(
        Func<CancellationToken, Task<TResult>> call,
        CancellationToken cancellationToken) => call(cancellationToken);

    public virtual void Dispose()
    {
    }

    protected static Task ForEachWithLimitAsync<T>(
        IReadOnlyList<T> items,
        int maxDegreeOfParallelism,
        Func<T, CancellationToken, ValueTask> body,
        CancellationToken cancellationToken)
    {
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism,
            CancellationToken = cancellationToken,
        };
        return Parallel.ForEachAsync(items, options, body);
    }

    protected static async Task<TResult> GateAsync<TResult>(
        SemaphoreSlim gate,
        Func<CancellationToken, Task<TResult>> call,
        CancellationToken cancellationToken)
    {
        // Waiting for a permit is not part of the dependency's timeout: the
        // timeout starts inside 'call', after the permit is held.
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await call(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public static ConcurrencyStrategy Create(LabOptions options) => options.Strategy switch
    {
        StrategyKind.C1 => new RuntimeDefaultStrategy(),
        StrategyKind.C2 => new SingleLimitStrategy(options.ItemParallelism),
        StrategyKind.C3 => new SharedLimiterStrategy(options.ItemParallelism, options.SharedLimit),
        StrategyKind.C4 => new PerDependencyStrategy(options.ItemParallelism, options.EnrichmentLimit, options.RecordsLimit),
        _ => throw new ArgumentOutOfRangeException(nameof(options)),
    };
}

/// <summary>C1: no options at all, so the runtime picks Environment.ProcessorCount.</summary>
public sealed class RuntimeDefaultStrategy : ConcurrencyStrategy
{
    public override string Name => "C1-runtime-default";

    public override Task ForEachItemAsync<T>(
        IReadOnlyList<T> items,
        Func<T, CancellationToken, ValueTask> body,
        CancellationToken cancellationToken)
        => Parallel.ForEachAsync(items, cancellationToken, body);
}

/// <summary>C2: one explicit number for everything; dependencies are not gated.</summary>
public sealed class SingleLimitStrategy(int itemParallelism) : ConcurrencyStrategy
{
    public override string Name => "C2-single-limit";

    public override Task ForEachItemAsync<T>(
        IReadOnlyList<T> items,
        Func<T, CancellationToken, ValueTask> body,
        CancellationToken cancellationToken)
        => ForEachWithLimitAsync(items, itemParallelism, body, cancellationToken);
}

/// <summary>C3: one semaphore shared by every dependency call.</summary>
public sealed class SharedLimiterStrategy(int itemParallelism, int sharedLimit) : ConcurrencyStrategy
{
    private readonly SemaphoreSlim _shared = new(sharedLimit, sharedLimit);

    public override string Name => "C3-shared-limiter";

    public override Task ForEachItemAsync<T>(
        IReadOnlyList<T> items,
        Func<T, CancellationToken, ValueTask> body,
        CancellationToken cancellationToken)
        => ForEachWithLimitAsync(items, itemParallelism, body, cancellationToken);

    public override Task<TResult> CallEnrichmentAsync<TResult>(
        Func<CancellationToken, Task<TResult>> call,
        CancellationToken cancellationToken) => GateAsync(_shared, call, cancellationToken);

    public override Task<TResult> CallRecordsAsync<TResult>(
        Func<CancellationToken, Task<TResult>> call,
        CancellationToken cancellationToken) => GateAsync(_shared, call, cancellationToken);

    public override void Dispose() => _shared.Dispose();
}

/// <summary>C4: each dependency has its own budget, sized for that dependency.</summary>
public sealed class PerDependencyStrategy(int itemParallelism, int enrichmentLimit, int recordsLimit) : ConcurrencyStrategy
{
    private readonly SemaphoreSlim _enrichment = new(enrichmentLimit, enrichmentLimit);
    private readonly SemaphoreSlim _records = new(recordsLimit, recordsLimit);

    public override string Name => "C4-per-dependency";

    public override Task ForEachItemAsync<T>(
        IReadOnlyList<T> items,
        Func<T, CancellationToken, ValueTask> body,
        CancellationToken cancellationToken)
        => ForEachWithLimitAsync(items, itemParallelism, body, cancellationToken);

    public override Task<TResult> CallEnrichmentAsync<TResult>(
        Func<CancellationToken, Task<TResult>> call,
        CancellationToken cancellationToken) => GateAsync(_enrichment, call, cancellationToken);

    public override Task<TResult> CallRecordsAsync<TResult>(
        Func<CancellationToken, Task<TResult>> call,
        CancellationToken cancellationToken) => GateAsync(_records, call, cancellationToken);

    public override void Dispose()
    {
        _enrichment.Dispose();
        _records.Dispose();
    }
}
