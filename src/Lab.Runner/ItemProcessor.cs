using System.Diagnostics;

namespace Lab.Runner;

public enum ItemStatus
{
    /// <summary>Every enrichment part arrived and the system of record accepted the write.</summary>
    Complete,

    /// <summary>At least one part is missing; the record was written and explicitly marked incomplete.</summary>
    Incomplete,

    /// <summary>The system of record answered 503.</summary>
    WriteRejected,

    /// <summary>The write timed out or failed for another reason.</summary>
    Failed,
}

/// <summary>
/// Processing is identical for every strategy. A failed part never aborts the
/// item and never gets written as if it were complete.
/// </summary>
public sealed class ItemProcessor(
    IEnrichmentClient enrichment,
    IRecordStore records,
    ConcurrencyStrategy strategy,
    LabOptions options,
    LatencyRecorder latencies)
{
    public async Task<ItemStatus> ProcessAsync(int itemId, CancellationToken cancellationToken)
    {
        var missingParts = 0;
        for (var part = 0; part < options.CallsPerItem; part++)
        {
            var currentPart = part;
            try
            {
                await strategy.CallEnrichmentAsync(
                    callToken => CallWithTimeoutAsync(
                        token => enrichment.GetAsync(itemId, currentPart, token),
                        options.EnrichmentTimeout,
                        latencies.RecordEnrichment,
                        callToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                missingParts++;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or System.Text.Json.JsonException)
            {
                missingParts++;
            }
        }

        var complete = missingParts == 0;
        try
        {
            // Latency is recorded only for accepted writes: an instant 503 would
            // otherwise make the overloading strategies look faster.
            var outcome = await strategy.CallRecordsAsync(
                callToken => CallWithTimeoutAsync(
                    token => records.WriteAsync(itemId, complete, token),
                    options.RecordsTimeout,
                    (elapsed, returned, result) =>
                    {
                        if (returned && result == WriteOutcome.Accepted)
                        {
                            latencies.RecordRecords(elapsed);
                        }
                    },
                    callToken),
                cancellationToken).ConfigureAwait(false);

            if (outcome == WriteOutcome.Rejected)
            {
                return ItemStatus.WriteRejected;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ItemStatus.Failed;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return ItemStatus.Failed;
        }

        return complete ? ItemStatus.Complete : ItemStatus.Incomplete;
    }

    /// <summary>Enrichment: every call is timed, successful or not.</summary>
    private static Task<TResult> CallWithTimeoutAsync<TResult>(
        Func<CancellationToken, Task<TResult>> call,
        TimeSpan timeout,
        Action<TimeSpan> record,
        CancellationToken cancellationToken)
        => CallWithTimeoutAsync(call, timeout, (elapsed, _, _) => record(elapsed), cancellationToken);

    /// <summary>
    /// The timeout starts here, after the strategy's permit is held. The callback
    /// receives whether the call returned and, if so, its result, so the caller can
    /// decide what to record.
    /// </summary>
    private static async Task<TResult> CallWithTimeoutAsync<TResult>(
        Func<CancellationToken, Task<TResult>> call,
        TimeSpan timeout,
        Action<TimeSpan, bool, TResult?> record,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var started = Stopwatch.GetTimestamp();
        var returned = false;
        TResult? result = default;
        try
        {
            result = await call(timeoutSource.Token).ConfigureAwait(false);
            returned = true;
            return result;
        }
        finally
        {
            record(Stopwatch.GetElapsedTime(started), returned, result);
        }
    }
}
