using System.Globalization;
using Lab.Summary;

namespace Lab.Tests;

public sealed class SummaryReportTests
{
    private const string Header =
        "strategy,scenario,processor_count,duration_s,goodput_items_s,complete,incomplete,write_rejected,failed,"
        + "stub_enrich_max_inflight,stub_records_max_inflight,stub_records_offered_max,stub_records_rejected,stub_records_calls";

    private sealed record Run(
        string Strategy, string Scenario, int Cpus = 1, double Duration = 10, double Goodput = 20,
        int EnrichMax = 24, int OfferedMax = 6, int Rejected = 0, int Calls = 200)
    {
        public string ToCsv() => string.Join(',',
            Strategy, Scenario, Cpus.ToString(CultureInfo.InvariantCulture),
            Duration.ToString(CultureInfo.InvariantCulture), Goodput.ToString(CultureInfo.InvariantCulture),
            "200", "0", Rejected.ToString(CultureInfo.InvariantCulture), "0",
            EnrichMax.ToString(CultureInfo.InvariantCulture), Math.Min(OfferedMax, 12).ToString(CultureInfo.InvariantCulture),
            OfferedMax.ToString(CultureInfo.InvariantCulture), Rejected.ToString(CultureInfo.InvariantCulture),
            Calls.ToString(CultureInfo.InvariantCulture));
    }

    // The names the runner writes (ConcurrencyStrategy.Name); the checks match them by prefix.
    private const string C1 = "C1-runtime-default";
    private const string C2 = "C2-single-limit";
    private const string C3 = "C3-shared-limiter";
    private const string C4 = "C4-per-dependency";

    private static string Summarize(params Run[] runs) =>
        SummaryReport.Render(ResultsCsv.Parse(Header + "\n" + string.Join("\n", runs.Select(r => r.ToCsv())) + "\n"));

    private static string CheckLine(string summary, string prefix) =>
        Assert.Single(summary.Split('\n'), line => line.StartsWith(prefix, StringComparison.Ordinal));

    [Fact]
    public void Median_averages_the_two_middle_values_for_an_even_count()
    {
        Assert.Equal(2.5, SummaryReport.Median([10, 1, 3, 2]));
        Assert.Equal(3, SummaryReport.Median([5, 1, 3]));
    }

    [Fact]
    public void The_table_shows_the_median_and_the_range_of_each_cell()
    {
        var summary = Summarize(
            new Run(C2, "healthy", Duration: 1), new Run(C2, "healthy", Duration: 2),
            new Run(C2, "healthy", Duration: 3), new Run(C2, "healthy", Duration: 10));

        Assert.Contains("| C2-single-limit | healthy | 1 | 4 | 2.5 (1–10) | 20.00 |", summary);
    }

    [Fact]
    public void The_table_is_sorted_by_strategy_then_scenario_then_cpus()
    {
        var summary = Summarize(
            new Run(C4, "records-degraded"), new Run(C4, "healthy"), new Run(C1, "healthy", Cpus: 2), new Run(C1, "healthy", Cpus: 1));

        var cells = summary.Split('\n')
            .Where(line => line.StartsWith("| C", StringComparison.Ordinal))
            .Select(line => string.Join(" ", line.Split('|', StringSplitOptions.TrimEntries)[1..4]));
        Assert.Equal(
            [$"{C1} healthy 1", $"{C1} healthy 2", $"{C4} healthy 1", $"{C4} records-degraded 1"],
            cells);
    }

    [Fact]
    public void H1_holds_when_C1_tracks_the_processor_count_and_slows_down_at_one_cpu()
    {
        var summary = Summarize(
            new Run(C1, "healthy", Cpus: 1, Duration: 17, EnrichMax: 1),
            new Run(C1, "healthy", Cpus: 2, Duration: 10, EnrichMax: 2));

        // A ratio of exactly 1.7 is the pre-registered threshold and holds.
        Assert.Equal(
            "- H1 (C1 follows the CPU limit): HOLDS. Max enrichment in flight at 1 CPU: 1–1; at 2 CPUs: 2–2; duration ratio 1 CPU / 2 CPUs: 1.70.",
            CheckLine(summary, "- H1"));
    }

    [Theory]
    [InlineData(16.9, 1, 2)] // duration ratio 1.69, just below 1.7
    [InlineData(20, 2, 2)] // ratio is fine, but C1 used 2 enrichment calls at 1 CPU
    [InlineData(20, 1, 1)] // ratio is fine, but C1 used 1 enrichment call at 2 CPUs
    public void H1_does_not_hold_when_the_ratio_or_the_in_flight_count_is_off(
        double durationAtOneCpu, int enrichMaxAtOneCpu, int enrichMaxAtTwoCpus)
    {
        var summary = Summarize(
            new Run(C1, "healthy", Cpus: 1, Duration: durationAtOneCpu, EnrichMax: enrichMaxAtOneCpu),
            new Run(C1, "healthy", Cpus: 2, Duration: 10, EnrichMax: enrichMaxAtTwoCpus));

        Assert.StartsWith("- H1 (C1 follows the CPU limit): DOES NOT HOLD.", CheckLine(summary, "- H1"));
    }

    [Theory]
    [InlineData(C2, "C2")]
    [InlineData(C3, "C3")]
    public void H2_holds_when_the_strategy_offers_more_than_twelve_writes_and_gets_503s(string strategy, string id)
    {
        var summary = Summarize(new Run(strategy, "records-degraded", OfferedMax: 13, Rejected: 5));

        Assert.Equal(
            $"- H2 ({id} overloads the system of record when it slows down): HOLDS. Median offered max 13 (limit 12); median 503s 5.",
            CheckLine(summary, $"- H2 ({id}"));
    }

    [Theory]
    [InlineData(C2, "C2", 12, 5)] // offered exactly the limit: not above it
    [InlineData(C3, "C3", 20, 0)] // offered too much, but no 503
    public void H2_does_not_hold_without_both_overload_and_503s(string strategy, string id, int offeredMax, int rejected)
    {
        var summary = Summarize(new Run(strategy, "records-degraded", OfferedMax: offeredMax, Rejected: rejected));

        Assert.StartsWith(
            $"- H2 ({id} overloads the system of record when it slows down): DOES NOT HOLD.",
            CheckLine(summary, $"- H2 ({id}"));
    }

    [Fact]
    public void H3_holds_when_C4_stays_within_capacity_in_every_run()
    {
        var summary = Summarize(
            new Run(C4, "healthy", OfferedMax: 6), new Run(C4, "records-degraded", OfferedMax: 5));

        Assert.Equal(
            "- H3 (design check: C4 never offers the system of record more than its capacity, never sees a 503): HOLDS across 2 runs. Offered max range 5–6; 503s range 0–0.",
            CheckLine(summary, "- H3"));
    }

    [Theory]
    [InlineData(7, 0)]
    [InlineData(6, 1)]
    public void H3_does_not_hold_when_any_C4_run_exceeds_capacity_or_sees_a_503(int offeredMax, int rejected)
    {
        var summary = Summarize(
            new Run(C4, "healthy", OfferedMax: 6),
            new Run(C4, "records-degraded", OfferedMax: offeredMax, Rejected: rejected));

        Assert.Contains("DOES NOT HOLD (harness bug: investigate before anything else) across 2 runs.", CheckLine(summary, "- H3"));
    }

    [Fact]
    public void Gate_passes_with_enough_C3_503s_and_C4_goodput_at_the_threshold()
    {
        var summary = Summarize(
            new Run(C3, "records-degraded", Goodput: 10, OfferedMax: 20, Rejected: 2, Calls: 200),
            new Run(C4, "records-degraded", Goodput: 9.5));

        Assert.Equal(
            "- Gate (records-degraded, same total permits): PASS. C3 median 503 share 1.0% (needs >= 1%); C4/C3 median goodput 0.95 (needs >= 0.95).",
            CheckLine(summary, "- Gate (records-degraded"));
        Assert.Equal("- **Gate overall: PASS (the central comparison is supported)**", CheckLine(summary, "- **Gate overall"));
    }

    [Theory]
    [InlineData(1, 10)] // 503 share 0.5%
    [InlineData(10, 9.49)] // C4/C3 goodput 0.949, just below 0.95
    public void Gate_fails_when_C3_sees_too_few_503s_or_C4_loses_goodput(int c3Rejected, double c4Goodput)
    {
        var summary = Summarize(
            new Run(C3, "records-degraded", Goodput: 10, OfferedMax: 20, Rejected: c3Rejected, Calls: 200),
            new Run(C4, "records-degraded", Goodput: c4Goodput));

        Assert.StartsWith("- Gate (records-degraded, same total permits): FAIL.", CheckLine(summary, "- Gate (records-degraded"));
        Assert.Equal("- **Gate overall: FAIL (the central comparison is not supported)**", CheckLine(summary, "- **Gate overall"));
    }

    [Fact]
    public void Missing_cells_are_reported_as_missing_runs()
    {
        var summary = Summarize(new Run(C1, "healthy", Cpus: 1, EnrichMax: 1), new Run(C2, "healthy"));

        Assert.Equal("- H1: missing runs.", CheckLine(summary, "- H1"));
        Assert.Equal("- H2 (C2): missing runs.", CheckLine(summary, "- H2 (C2"));
        Assert.Equal("- H2 (C3): missing runs.", CheckLine(summary, "- H2 (C3"));
        Assert.Equal("- Gate: missing runs.", CheckLine(summary, "- Gate"));

        // Same as the earlier script: with no C4 run at all there is no H3 line, and no
        // enrichment-degraded line without both C3 and C4.
        Assert.DoesNotContain(summary.Split('\n'), line => line.StartsWith("- H3", StringComparison.Ordinal));
        Assert.DoesNotContain(summary.Split('\n'), line => line.StartsWith("- Reported", StringComparison.Ordinal));
    }

    [Fact]
    public void Gate_counts_the_503_share_against_one_call_when_C3_made_no_calls()
    {
        var summary = Summarize(
            new Run(C3, "records-degraded", Goodput: 10, Rejected: 0, Calls: 0),
            new Run(C4, "records-degraded", Goodput: 10));

        Assert.StartsWith(
            "- Gate (records-degraded, same total permits): FAIL. C3 median 503 share 0.0% (needs >= 1%)",
            CheckLine(summary, "- Gate (records-degraded"));
    }

    [Fact]
    public void The_enrichment_degraded_trade_off_is_reported_without_a_threshold()
    {
        var summary = Summarize(
            new Run(C3, "enrichment-degraded", Goodput: 10, OfferedMax: 20, Rejected: 3),
            new Run(C4, "enrichment-degraded", Goodput: 12));

        Assert.Equal(
            "- Reported, no threshold (enrichment-degraded): C4/C3 median goodput 1.20; C3 median 503s 3; C4 median 503s 0.",
            CheckLine(summary, "- Reported"));
    }

    [Theory]
    [InlineData(0.125, 2, "0.12")] // exact tie: rounds to even
    [InlineData(12.25, 1, "12.2")]
    [InlineData(2.675, 2, "2.67")] // the double is just below 2.675
    [InlineData(0.375, 2, "0.38")]
    [InlineData(-0.01, 1, "-0.0")]
    public void Fixed_rounds_the_exact_binary_value_with_ties_to_even(double value, int decimals, string expected)
    {
        Assert.Equal(expected, Formatting.Fixed(value, decimals));
    }

    [Theory]
    [InlineData(6, "6")]
    [InlineData(6.5, "6.5")]
    [InlineData(123.4567, "123.457")]
    [InlineData(999999.5, "1e+06")]
    [InlineData(1234567, "1.23457e+06")]
    [InlineData(0.0001, "0.0001")]
    [InlineData(0.00001, "1e-05")]
    [InlineData(0, "0")]
    public void General_keeps_six_significant_digits_without_trailing_zeros(double value, string expected)
    {
        Assert.Equal(expected, Formatting.General(value));
    }

    [Fact]
    public void A_row_with_the_wrong_number_of_fields_is_rejected()
    {
        Assert.Throws<FormatException>(() => ResultsCsv.Parse("a,b\n1,2\n3\n"));
    }
}
