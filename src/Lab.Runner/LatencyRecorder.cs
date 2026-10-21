using System.Collections.Concurrent;

namespace Lab.Runner;

/// <summary>Client-side latency per dependency call, measured after the permit is held.</summary>
public sealed class LatencyRecorder
{
    private readonly ConcurrentQueue<double> _enrichmentMs = new();
    private readonly ConcurrentQueue<double> _recordsMs = new();

    public void RecordEnrichment(TimeSpan elapsed) => _enrichmentMs.Enqueue(elapsed.TotalMilliseconds);

    public void RecordRecords(TimeSpan elapsed) => _recordsMs.Enqueue(elapsed.TotalMilliseconds);

    public double EnrichmentPercentile(double p) => Percentile(_enrichmentMs, p);

    public double RecordsPercentile(double p) => Percentile(_recordsMs, p);

    private static double Percentile(IEnumerable<double> values, double p)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        if (sorted.Length == 0)
        {
            return double.NaN;
        }

        // Nearest-rank method.
        var rank = (int)Math.Ceiling(p / 100.0 * sorted.Length);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }
}
