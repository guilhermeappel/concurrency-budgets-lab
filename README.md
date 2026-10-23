# Concurrency budgets lab

A small, reproducible lab for one claim: **for I/O-bound work, concurrency should be sized per
dependency, not by the number of CPU cores.** It runs the same workload against two simulated
dependencies (a slow enrichment API and a fragile system of record) with four ways of bounding
concurrency, inside containers with a fixed CPU limit.

All numbers this lab produces are **lab results** from simulated dependencies. They describe the
model in `src/Lab.Stubs/StubState.cs`, not any production system.

## What it compares

| ID | Strategy |
|---|---|
| C1 | `Parallel.ForEachAsync` with no options: the runtime picks `Environment.ProcessorCount` |
| C2 | One explicit `MaxDegreeOfParallelism` for everything |
| C3 | One `SemaphoreSlim` shared by every dependency call |
| C4 | One budget per dependency, sized for that dependency |

The hypotheses, thresholds and matrix were written down before the first run:
see [PREREGISTRATION.md](PREREGISTRATION.md).

## Layout

- `src/Lab.Stubs`: ASP.NET Core minimal API that simulates both dependencies and counts how many
  calls are in flight at each one.
- `src/Lab.Runner`: console app that processes a batch with one strategy and appends a CSV row.
  `ConcurrencyStrategy.cs` is where the four strategies differ; `ItemProcessor.cs` is shared.
- `tests/Lab.Tests`: unit tests with in-memory fakes (no HTTP, no Docker).
- `scripts/run-matrix.sh`: runs the pre-registered matrix with Docker Compose.
- `src/Lab.Summary`: console app that turns `results/results.csv` into `results/summary.md` and
  checks the pre-registered hypotheses. The checks and thresholds are in `SummaryReport.cs`.

## Requirements

- .NET 10 SDK (to build, run the tests and write the summary)
- Docker with Compose v2, on a machine with at least 2 CPUs

## Run

```bash
dotnet test tests/Lab.Tests
REPS=5 ./scripts/run-matrix.sh
```

A single run, without Docker (the CPU count is overridden with `DOTNET_PROCESSOR_COUNT`, which
changes what the runtime reports but does not throttle the process the way a container limit does):

```bash
dotnet run --project src/Lab.Stubs &
DOTNET_PROCESSOR_COUNT=1 dotnet run --project src/Lab.Runner -- --strategy C1 --scenario healthy
```

## Results

55 runs on 2026-10-21 (16:27 to 16:48 UTC), at commit `1a525f873d476d0abed8981c3468ad36e771ff18`
(`results/commit.txt`), with `REPS=5 ./scripts/run-matrix.sh`. The raw rows are in
[results/results.csv](results/results.csv) and the checks of the pre-registered hypotheses are in
[results/summary.md](results/summary.md). This is Table 1 from that file.

Goodput counts only items whose write was accepted (complete or marked incomplete). 'Accepted at SoR' is the peak of writes the system of record accepted (processing + queued, at most 12); 'Offered to SoR' is the peak of concurrent writes it was offered, including rejected ones.

| Strategy | Scenario | CPUs | Runs | Duration s (range) | Goodput items/s | Complete | Incomplete | Rejected writes | Failed | Enrichment max in flight | Accepted at SoR (max) | Offered to SoR (max) | 503s |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| C1-runtime-default | healthy | 1 | 5 | 83.6 (83.342–86.422) | 2.39 | 200 | 0 | 0 | 0 | 1 | 1 | 1 | 0 |
| C1-runtime-default | healthy | 2 | 5 | 42.1 (41.662–43.05) | 4.76 | 200 | 0 | 0 | 0 | 2 | 2 | 2 | 0 |
| C2-single-limit | enrichment-degraded | 1 | 5 | 26.2 (26.102–27.299) | 7.63 | 163 | 37 | 0 | 0 | 32 | 4 | 4 | 0 |
| C2-single-limit | healthy | 1 | 5 | 2.9 (2.857–3.006) | 67.86 | 200 | 0 | 0 | 0 | 32 | 11 | 11 | 0 |
| C2-single-limit | records-degraded | 1 | 5 | 3.9 (3.87–3.959) | 34.35 | 136 | 0 | 64 | 0 | 32 | 12 | 13 | 64 |
| C3-shared-limiter | enrichment-degraded | 1 | 5 | 28.2 (27.012–28.61) | 7.09 | 159 | 41 | 0 | 0 | 30 | 4 | 4 | 0 |
| C3-shared-limiter | healthy | 1 | 5 | 3.1 (3.02–3.092) | 65.32 | 200 | 0 | 0 | 0 | 30 | 12 | 12 | 0 |
| C3-shared-limiter | records-degraded | 1 | 5 | 4.2 (4.121–4.283) | 34.01 | 143 | 0 | 57 | 0 | 30 | 12 | 13 | 57 |
| C4-per-dependency | enrichment-degraded | 1 | 5 | 34.5 (33.986–34.904) | 5.79 | 158 | 42 | 0 | 0 | 24 | 3 | 3 | 0 |
| C4-per-dependency | healthy | 1 | 5 | 3.4 (3.408–3.512) | 58.38 | 200 | 0 | 0 | 0 | 24 | 6 | 6 | 0 |
| C4-per-dependency | records-degraded | 1 | 5 | 5.7 (5.663–5.791) | 35.08 | 200 | 0 | 0 | 0 | 24 | 6 | 6 | 0 |

The article calls items "records" and goodput "accepted records/s".

## How this was built

I specified the experiments and ran them on my machine with Claude Code. Most of the lab code was
written with Anthropic's Claude from my specification.

## License

MIT. See [LICENSE](LICENSE).
