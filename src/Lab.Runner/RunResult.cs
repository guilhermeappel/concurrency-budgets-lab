using System.Globalization;
using System.Text;

namespace Lab.Runner;

public sealed record RunResult(
    string RunId,
    DateTimeOffset TimestampUtc,
    string Strategy,
    string Scenario,
    int ProcessorCount,
    LabOptions Options,
    double DurationSeconds,
    int Complete,
    int Incomplete,
    int WriteRejected,
    int Failed,
    double EnrichmentP50Ms,
    double EnrichmentP95Ms,
    double RecordsP50Ms,
    double RecordsP95Ms,
    StubStats Stubs)
{
    /// <summary>
    /// Goodput: items whose write the system of record accepted (complete or explicitly
    /// incomplete) per second. Rejected and failed items are not counted, so a strategy
    /// cannot look fast by getting its writes refused.
    /// </summary>
    public double GoodputItemsPerSecond => (Complete + Incomplete) / DurationSeconds;

    private static readonly string[] Columns =
    [
        "run_id", "timestamp_utc", "strategy", "scenario", "processor_count",
        "items", "calls_per_item", "item_parallelism", "shared_limit", "enrichment_limit", "records_limit",
        "duration_s", "goodput_items_s",
        "complete", "incomplete", "write_rejected", "failed",
        "enrich_p50_ms", "enrich_p95_ms", "records_p50_ms", "records_p95_ms",
        "stub_enrich_calls", "stub_enrich_max_inflight", "stub_enrich_hangs",
        "stub_records_calls", "stub_records_max_inflight", "stub_records_offered_max", "stub_records_rejected",
    ];

    public string ToCsvRow()
    {
        string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        string I(int value) => value.ToString(CultureInfo.InvariantCulture);

        return string.Join(',',
            RunId, TimestampUtc.ToString("O", CultureInfo.InvariantCulture), Strategy, Scenario, I(ProcessorCount),
            I(Options.Items), I(Options.CallsPerItem), I(Options.ItemParallelism), I(Options.SharedLimit),
            I(Options.EnrichmentLimit), I(Options.RecordsLimit),
            F(DurationSeconds), F(GoodputItemsPerSecond),
            I(Complete), I(Incomplete), I(WriteRejected), I(Failed),
            F(EnrichmentP50Ms), F(EnrichmentP95Ms), F(RecordsP50Ms), F(RecordsP95Ms),
            I(Stubs.EnrichmentCalls), I(Stubs.EnrichmentMaxInFlight), I(Stubs.EnrichmentHangs),
            I(Stubs.RecordsCalls), I(Stubs.RecordsMaxInFlight), I(Stubs.RecordsOfferedMax), I(Stubs.RecordsRejected));
    }

    public static void AppendCsv(string path, RunResult result)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var writeHeader = !File.Exists(path) || new FileInfo(path).Length == 0;
        var builder = new StringBuilder();
        if (writeHeader)
        {
            builder.AppendLine(string.Join(',', Columns));
        }

        builder.AppendLine(result.ToCsvRow());
        File.AppendAllText(path, builder.ToString());
    }

    public string ToSummary() =>
        $"""
        run {RunId} | {Strategy} | {Scenario} | ProcessorCount={ProcessorCount}
          duration {DurationSeconds:0.00} s, goodput {GoodputItemsPerSecond:0.00} items/s
          items: complete {Complete}, incomplete {Incomplete}, write rejected {WriteRejected}, failed {Failed}
          enrichment: max in flight {Stubs.EnrichmentMaxInFlight}, p50 {EnrichmentP50Ms:0} ms, p95 {EnrichmentP95Ms:0} ms
          system of record: max accepted (processing + queued) {Stubs.RecordsMaxInFlight}, offered max {Stubs.RecordsOfferedMax}, rejected {Stubs.RecordsRejected}
        """;
}
