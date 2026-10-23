# Lab results summary

Runs: 55. Values are medians across repetitions; ranges are min–max.

## Table 1: all runs

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

## Pre-registered checks

- H1 (C1 follows the CPU limit): HOLDS. Max enrichment in flight at 1 CPU: 1–1; at 2 CPUs: 2–2; duration ratio 1 CPU / 2 CPUs: 1.99.
- H2 (C2 overloads the system of record when it slows down): HOLDS. Median offered max 13 (limit 12); median 503s 64.
- H2 (C3 overloads the system of record when it slows down): HOLDS. Median offered max 13 (limit 12); median 503s 57.
- H3 (design check: C4 never offers the system of record more than its capacity, never sees a 503): HOLDS across 15 runs. Offered max range 3–6; 503s range 0–0.
- Gate (records-degraded, same total permits): PASS. C3 median 503 share 28.5% (needs >= 1%); C4/C3 median goodput 1.03 (needs >= 0.95).
- **Gate overall: PASS (the central comparison is supported)**
- Reported, no threshold (enrichment-degraded): C4/C3 median goodput 0.82; C3 median 503s 0; C4 median 503s 0.
