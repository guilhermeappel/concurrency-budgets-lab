# Pre-registration

Written before any matrix run. No lab results had been seen when this file was committed.
If anything here changes after the first run, add a line to the log at the bottom with
the date and the reason, and label every number it affects as "computed after the change".

Date committed: 2026-10-21

## What the lab models

Two simulated dependencies, defined in `src/Lab.Stubs/StubState.cs`:

- **Enrichment API**: latency-bound. Tolerates many concurrent calls. Healthy median 120 ms;
  degraded median 1,200 ms with 5% of calls hanging for 8 s (longer than the client's 2 s timeout).
- **System of record (SoR)**: capacity-bound. Processes 6 requests at a time, lets 6 more wait,
  and answers 503 beyond 12. Healthy service time median 30 ms; degraded 150 ms.

These shapes are a model, not measurements of any real system. The numbers the lab produces
describe the model under the stated limits.

Workload: 200 items; each item makes 3 enrichment calls one after another, then 1 write to the
SoR. A part that times out is not retried; the item is written and marked incomplete.
A write the SoR rejects (503) is not retried.

## Strategies

| ID | What bounds concurrency | Parameters |
|---|---|---|
| C1 | `Parallel.ForEachAsync` with no options (runtime default) | none |
| C2 | One explicit `MaxDegreeOfParallelism` for items | 32 |
| C3 | Items 32; one `SemaphoreSlim` shared by both dependencies | 30 permits |
| C4 | Items 32; one `SemaphoreSlim` per dependency | enrichment 24, SoR 6 |

C3 and C4 have the same total number of permits (30), so they differ only in how the permits are
split. C4's SoR budget equals the SoR's capacity on purpose: that is the sizing rule being tested.

## Metrics

- **Goodput**: items whose write the SoR accepted (complete or marked incomplete) per second.
  Rejected and failed items do not count.
- **Offered to SoR (max)**: peak number of concurrent writes the SoR was offered, including rejected ones.
- **Accepted at SoR (max)**: peak number of writes the SoR held at once (processing + queued; at most 12).
- **503s**: writes the SoR rejected.

## Matrix

- C1, healthy, with the runner limited to 1 CPU and to 2 CPUs (`docker --cpus`).
- C2, C3, C4 × {healthy, enrichment-degraded, records-degraded}, runner limited to 1 CPU.
- 5 repetitions; repetitions are the outer loop. Medians and min–max ranges are reported.

## Hypotheses and thresholds

- **H1** C1 follows the CPU limit: in every run, the enrichment API's max calls in flight equals
  the runner's `Environment.ProcessorCount` (1 at 1 CPU, 2 at 2 CPUs), and the median duration at
  1 CPU is at least 1.7 times the median at 2 CPUs.
- **H2** When the SoR slows down (records-degraded), a single item limit (C2) and a shared limiter (C3)
  overload it: median "offered to SoR" above 12 and median 503 count above 0.
- **H3** (design check, not a finding) C4 never offers the SoR more than 6 concurrent writes and
  never sees a 503, in any run or scenario. This follows from the configuration; if it fails, the
  harness has a bug and nothing else is reported until it is found.
- **Gate** (decides whether the article goes ahead), records-degraded only: C3's median 503s are at
  least 1% of its SoR calls **and** C4's median goodput is at least 0.95 times C3's. In words: with the
  same number of permits, splitting them per dependency protects the SoR without losing goodput.
  If the gate fails, the central comparison is not supported and the write-up is dropped.
- **Reported without a threshold**: healthy results for C2, C3, C4, and the C4/C3 goodput ratio in
  enrichment-degraded (C4 caps enrichment at 24 while C3 can use all 30 permits there, so C4 may be
  slower; that trade-off is reported as measured).

## What counts as a valid run

- A run counts only if the runner printed its summary and appended a CSV row.
- No run is deleted. A failed run is retried with the same parameters (up to 3 times) and every
  failure is written to `results/failures.log`.

## Log

- Before the first commit (2026-10-20): an independent code review traced the model with a separate
  Python simulation (not this lab, no HTTP or JIT). Because of what it showed, these were changed
  before any lab run: C3's shared limit from 16 to 30 (same total as C4, so the comparison is about
  splitting, not size); the metric from throughput to goodput (rejected writes no longer count as
  progress); H2 moved from the healthy to the records-degraded scenario (in the healthy model the SoR
  is not under pressure); the gate now tests protection at equal permits instead of raw speed;
  SoR latency is recorded only for accepted writes.
- 2026-10-23, after the runs: the summary's PASS line for the gate said "topic A stands", a label
  explained nowhere in the lab. It now says "the central comparison is supported", the counterpart
  of the FAIL line. Only that text changed in `src/Lab.Summary/SummaryReport.cs` and its test; no
  hypothesis, threshold, check or parameter changed, and no number is computed after the change.
  `results/summary.md` was regenerated from the unchanged `results/results.csv` with the same
  command `scripts/run-matrix.sh` uses, and differs from the earlier file only in that line.
<!-- NOTE (Guilherme): each change after the first run becomes a line here:
     date, what changed, why, and which numbers become "computed after the change". -->
