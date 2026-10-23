using System.Globalization;

namespace Lab.Summary;

using Row = IReadOnlyDictionary<string, string>;

/// <summary>
/// Turns the rows of results.csv into the tables the article uses, plus the pre-registered
/// hypothesis checks (see PREREGISTRATION.md). Thresholds live here and are frozen with it.
/// </summary>
public static class SummaryReport
{
    private const int RecordsCapacity = 6;
    private const int RecordsQueueLimit = 6;

    public static string Render(IReadOnlyList<Row> rows)
    {
        var groups = Group(rows);
        var output = new List<string>
        {
            "# Lab results summary\n",
            $"Runs: {rows.Count}. Values are medians across repetitions; ranges are min–max.\n",
            "## Table 1: all runs\n",
            "Goodput counts only items whose write was accepted (complete or marked incomplete). "
            + "'Accepted at SoR' is the peak of writes the system of record accepted (processing + queued, at most 12); "
            + "'Offered to SoR' is the peak of concurrent writes it was offered, including rejected ones.\n",
            "| Strategy | Scenario | CPUs | Runs | Duration s (range) | Goodput items/s | Complete | Incomplete | Rejected writes | Failed | Enrichment max in flight | Accepted at SoR (max) | Offered to SoR (max) | 503s |",
            "|---|---|---|---|---|---|---|---|---|---|---|---|---|---|",
        };

        var sorted = groups.OrderBy(g => g.Strategy, StringComparer.Ordinal)
            .ThenBy(g => g.Scenario, StringComparer.Ordinal)
            .ThenBy(g => g.Cpus, StringComparer.Ordinal);
        foreach (var g in sorted)
        {
            var r = g.Rows;
            output.Add(
                $"| {g.Strategy} | {g.Scenario} | {g.Cpus} | {r.Count} | {Formatting.Fixed(Med(r, "duration_s"), 1)} ({Range(r, "duration_s")}) "
                + $"| {Formatting.Fixed(Med(r, "goodput_items_s"), 2)} | {G(Med(r, "complete"))} | {G(Med(r, "incomplete"))} "
                + $"| {G(Med(r, "write_rejected"))} | {G(Med(r, "failed"))} | {G(Med(r, "stub_enrich_max_inflight"))} "
                + $"| {G(Med(r, "stub_records_max_inflight"))} | {G(Med(r, "stub_records_offered_max"))} | {G(Med(r, "stub_records_rejected"))} |");
        }

        output.Add("\n## Pre-registered checks\n");

        // Groups are searched in the order they first appear in results.csv.
        List<Row>? Find(string strategyPrefix, string scenario, int cpus) =>
            groups.FirstOrDefault(g => g.Strategy.StartsWith(strategyPrefix, StringComparison.Ordinal)
                                       && g.Scenario == scenario
                                       && g.Cpus == cpus.ToString(CultureInfo.InvariantCulture))?.Rows;

        const int limit = RecordsCapacity + RecordsQueueLimit;

        // H1
        var c1One = Find("C1", "healthy", 1);
        var c1Two = Find("C1", "healthy", 2);
        if (c1One is not null && c1Two is not null)
        {
            var ratio = Divide(Med(c1One, "duration_s"), Med(c1Two, "duration_s"));
            var h1 = c1One.All(r => Num(r, "stub_enrich_max_inflight") == 1)
                     && c1Two.All(r => Num(r, "stub_enrich_max_inflight") == 2)
                     && ratio >= 1.7;
            output.Add($"- H1 (C1 follows the CPU limit): {(h1 ? "HOLDS" : "DOES NOT HOLD")}. "
                       + $"Max enrichment in flight at 1 CPU: {Range(c1One, "stub_enrich_max_inflight")}; "
                       + $"at 2 CPUs: {Range(c1Two, "stub_enrich_max_inflight")}; duration ratio 1 CPU / 2 CPUs: {Formatting.Fixed(ratio, 2)}.");
        }
        else
        {
            output.Add("- H1: missing runs.");
        }

        // H2 (records-degraded only)
        foreach (var prefix in new[] { "C2", "C3" })
        {
            var g = Find(prefix, "records-degraded", 1);
            if (g is not null)
            {
                var holds = Med(g, "stub_records_offered_max") > limit && Med(g, "stub_records_rejected") > 0;
                output.Add($"- H2 ({prefix} overloads the system of record when it slows down): {(holds ? "HOLDS" : "DOES NOT HOLD")}. "
                           + $"Median offered max {G(Med(g, "stub_records_offered_max"))} (limit {limit}); median 503s {G(Med(g, "stub_records_rejected"))}.");
            }
            else
            {
                output.Add($"- H2 ({prefix}): missing runs.");
            }
        }

        // H3 (design check, true by construction when records_limit == capacity)
        var c4All = groups.Where(g => g.Strategy.StartsWith("C4", StringComparison.Ordinal)).SelectMany(g => g.Rows).ToList();
        if (c4All.Count > 0)
        {
            var holds = c4All.All(r => Num(r, "stub_records_offered_max") <= RecordsCapacity && Num(r, "stub_records_rejected") == 0);
            output.Add("- H3 (design check: C4 never offers the system of record more than its capacity, never sees a 503): "
                       + $"{(holds ? "HOLDS" : "DOES NOT HOLD (harness bug: investigate before anything else)")} across {c4All.Count} runs. "
                       + $"Offered max range {Range(c4All, "stub_records_offered_max")}; 503s range {Range(c4All, "stub_records_rejected")}.");
        }

        // Gate: same total permits (C3 shared 30 vs C4 24 + 6), records-degraded.
        var c3 = Find("C3", "records-degraded", 1);
        var c4 = Find("C4", "records-degraded", 1);
        if (c3 is not null && c4 is not null)
        {
            var c3RejectShare = Divide(Med(c3, "stub_records_rejected"), Math.Max(Med(c3, "stub_records_calls"), 1));
            var goodputRatio = Divide(Med(c4, "goodput_items_s"), Med(c3, "goodput_items_s"));
            var passed = c3RejectShare >= 0.01 && goodputRatio >= 0.95;
            output.Add($"- Gate (records-degraded, same total permits): {(passed ? "PASS" : "FAIL")}. "
                       + $"C3 median 503 share {Formatting.Percent(c3RejectShare, 1)} (needs >= 1%); C4/C3 median goodput {Formatting.Fixed(goodputRatio, 2)} (needs >= 0.95).");
            output.Add($"- **Gate overall: {(passed ? "PASS (the central comparison is supported)" : "FAIL (the central comparison is not supported)")}**");
        }
        else
        {
            output.Add("- Gate: missing runs.");
        }

        // Reported trade-off, no threshold.
        var c3e = Find("C3", "enrichment-degraded", 1);
        var c4e = Find("C4", "enrichment-degraded", 1);
        if (c3e is not null && c4e is not null)
        {
            output.Add("- Reported, no threshold (enrichment-degraded): C4/C3 median goodput "
                       + $"{Formatting.Fixed(Divide(Med(c4e, "goodput_items_s"), Med(c3e, "goodput_items_s")), 2)}; "
                       + $"C3 median 503s {G(Med(c3e, "stub_records_rejected"))}; C4 median 503s {G(Med(c4e, "stub_records_rejected"))}.");
        }

        return string.Join("\n", output) + "\n";
    }

    private sealed record RunGroup(string Strategy, string Scenario, string Cpus, List<Row> Rows);

    /// <summary>Groups by (strategy, scenario, processor_count), in order of first appearance.</summary>
    private static List<RunGroup> Group(IReadOnlyList<Row> rows)
    {
        var groups = new List<RunGroup>();
        foreach (var row in rows)
        {
            var group = groups.Find(g => g.Strategy == row["strategy"] && g.Scenario == row["scenario"] && g.Cpus == row["processor_count"]);
            if (group is null)
            {
                group = new RunGroup(row["strategy"], row["scenario"], row["processor_count"], []);
                groups.Add(group);
            }

            group.Rows.Add(row);
        }

        return groups;
    }

    private static double Num(Row row, string key) => double.Parse(row[key], NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>Median; for an even count, the mean of the two middle values.</summary>
    public static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0)
        {
            throw new InvalidOperationException("No values for a median.");
        }

        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private static double Med(IEnumerable<Row> rows, string key) => Median(rows.Select(r => Num(r, key)));

    private static string Range(IEnumerable<Row> rows, string key)
    {
        var values = rows.Select(r => Num(r, key)).ToList();
        return $"{G(values.Min())}–{G(values.Max())}";
    }

    private static string G(double value) => Formatting.General(value);

    /// <summary>Division that fails on a zero divisor instead of printing infinity.</summary>
    private static double Divide(double numerator, double denominator) =>
        denominator == 0 ? throw new DivideByZeroException("Median is zero; the ratio is undefined.") : numerator / denominator;
}
