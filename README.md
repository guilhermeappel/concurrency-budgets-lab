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

<!-- NOTA (Guilherme): depois do run, cole aqui a Tabela 1 de results/summary.md,
     a linha do commit (results/commit.txt) e a data. Apague esta nota. -->
Not run yet.

## How this was built

<!-- NOTA (Guilherme): ajuste ao que for verdade quando publicar. Sugestão abaixo. -->
I designed the experiment, the hypotheses and the strategies. The code was written with the help
of an AI assistant (Anthropic's Claude) from my specification; I reviewed, ran and can explain
every part of it.

## License

<!-- NOTA (Guilherme): escolha a licença (ex.: MIT) e crie o arquivo LICENSE com o seu nome. -->
