namespace Lab.Runner;

public enum StrategyKind
{
    C1, // runtime default (Parallel.ForEachAsync without options)
    C2, // one explicit MaxDegreeOfParallelism for everything
    C3, // one SemaphoreSlim shared by both dependencies
    C4, // one limit per dependency
}

public enum Scenario
{
    Healthy,
    EnrichmentDegraded,
    RecordsDegraded,
}

/// <summary>
/// Every concurrency number the lab uses lives here, is typed, and is validated
/// before the first request is sent. Nothing is derived from the CPU count
/// except the runtime default that strategy C1 demonstrates.
/// </summary>
public sealed record LabOptions
{
    public StrategyKind Strategy { get; init; } = StrategyKind.C4;
    public Scenario Scenario { get; init; } = Scenario.Healthy;
    public int Items { get; init; } = 200;
    public int CallsPerItem { get; init; } = 3;

    /// <summary>Items processed at the same time (C2, C3 and C4).</summary>
    public int ItemParallelism { get; init; } = 32;

    /// <summary>
    /// Permits shared by both dependencies (C3 only). Defaults to the same total as C4
    /// (EnrichmentLimit + RecordsLimit), so C3 and C4 differ only in how the permits
    /// are split, not in how many there are.
    /// </summary>
    public int SharedLimit { get; init; } = 30;

    /// <summary>Concurrent calls allowed to the enrichment API (C4 only).</summary>
    public int EnrichmentLimit { get; init; } = 24;

    /// <summary>Concurrent calls allowed to the system of record (C4 only).</summary>
    public int RecordsLimit { get; init; } = 6;

    public TimeSpan EnrichmentTimeout { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan RecordsTimeout { get; init; } = TimeSpan.FromSeconds(3);

    public Uri StubsBaseAddress { get; init; } = new("http://localhost:5080/");
    public string RunId { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public string? OutputCsv { get; init; }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Items <= 0) errors.Add("--items must be greater than zero.");
        if (CallsPerItem <= 0) errors.Add("--calls-per-item must be greater than zero.");
        if (ItemParallelism <= 0) errors.Add("--item-parallelism must be greater than zero.");
        if (SharedLimit <= 0) errors.Add("--shared-limit must be greater than zero.");
        if (EnrichmentLimit <= 0) errors.Add("--enrichment-limit must be greater than zero.");
        if (RecordsLimit <= 0) errors.Add("--records-limit must be greater than zero.");
        if (EnrichmentTimeout <= TimeSpan.Zero) errors.Add("--enrichment-timeout-ms must be greater than zero.");
        if (RecordsTimeout <= TimeSpan.Zero) errors.Add("--records-timeout-ms must be greater than zero.");
        if (Strategy == StrategyKind.C4 && EnrichmentLimit > ItemParallelism)
        {
            // Each item calls the enrichment API one part at a time, so the item loop
            // can never have more than ItemParallelism enrichment calls in flight.
            errors.Add("--enrichment-limit is larger than --item-parallelism, so it would never be reached.");
        }
        return errors;
    }

    public static LabOptions Parse(IReadOnlyList<string> args)
    {
        var options = new LabOptions();
        for (var i = 0; i < args.Count; i++)
        {
            var key = args[i];
            if (i + 1 >= args.Count)
            {
                throw new ArgumentException($"Missing value for {key}.");
            }

            var value = args[++i];
            options = key switch
            {
                "--strategy" => options with { Strategy = Enum.Parse<StrategyKind>(value, ignoreCase: true) },
                "--scenario" => options with { Scenario = ParseScenario(value) },
                "--items" => options with { Items = int.Parse(value) },
                "--calls-per-item" => options with { CallsPerItem = int.Parse(value) },
                "--item-parallelism" => options with { ItemParallelism = int.Parse(value) },
                "--shared-limit" => options with { SharedLimit = int.Parse(value) },
                "--enrichment-limit" => options with { EnrichmentLimit = int.Parse(value) },
                "--records-limit" => options with { RecordsLimit = int.Parse(value) },
                "--enrichment-timeout-ms" => options with { EnrichmentTimeout = TimeSpan.FromMilliseconds(int.Parse(value)) },
                "--records-timeout-ms" => options with { RecordsTimeout = TimeSpan.FromMilliseconds(int.Parse(value)) },
                "--stubs" => options with { StubsBaseAddress = new Uri(value.EndsWith('/') ? value : value + "/") },
                "--run-id" => options with { RunId = value },
                "--out" => options with { OutputCsv = value },
                _ => throw new ArgumentException($"Unknown option {key}."),
            };
        }
        return options;
    }

    public static Scenario ParseScenario(string value) => value.ToLowerInvariant() switch
    {
        "healthy" => Scenario.Healthy,
        "enrichment-degraded" => Scenario.EnrichmentDegraded,
        "records-degraded" => Scenario.RecordsDegraded,
        _ => throw new ArgumentException($"Unknown scenario {value}."),
    };

    public static string ScenarioName(Scenario scenario) => scenario switch
    {
        Scenario.Healthy => "healthy",
        Scenario.EnrichmentDegraded => "enrichment-degraded",
        Scenario.RecordsDegraded => "records-degraded",
        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };
}
