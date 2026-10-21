#!/usr/bin/env bash
# Runs the pre-registered matrix (see PREREGISTRATION.md) and writes
# results/results.csv, results/failures.log and results/summary.md.
#
# Usage:  REPS=5 ./scripts/run-matrix.sh
# Resume: RESUME=1 REPS=5 ./scripts/run-matrix.sh   (only after an interrupted run; see the log rule)
set -euo pipefail
cd "$(dirname "$0")/.."

REPS="${REPS:-5}"
RESUME="${RESUME:-0}"
OUT=/results/results.csv
mkdir -p results
export UID
# Git Bash on Windows rewrites arguments like /results/results.csv into Windows paths before
# docker sees them; this turns that off. Other shells ignore it.
export MSYS_NO_PATHCONV=1

if [[ -s results/results.csv && "$RESUME" != "1" ]]; then
  echo "results/results.csv already exists. Move it away, or set RESUME=1 to continue an interrupted matrix." >&2
  exit 1
fi

# Build the summary before any run, so a missing SDK or a build error stops here and not after
# the whole matrix.
dotnet build src/Lab.Summary -c Release --nologo -v quiet

git rev-parse HEAD > results/commit.txt 2>/dev/null || echo "not a git checkout" > results/commit.txt
docker compose --profile runner build
docker compose up -d stubs

# One run of one cell. A run that fails is retried with the same parameters
# (up to 3 times) and every failure is written to results/failures.log.
run_once() {
  local service="$1" rep="$2"; shift 2
  local run_id="${service}-$(echo "$*" | tr ' ' '_' | tr -d '-')-r${rep}"
  if [[ "$RESUME" == "1" ]] && grep -q "^${run_id}," results/results.csv 2>/dev/null; then
    return 0
  fi
  local attempt
  for attempt in 1 2 3; do
    if docker compose run --rm -T "$service" --stubs http://stubs:5080 --out "$OUT" --run-id "$run_id" "$@"; then
      return 0
    fi
    echo "$(date -Is) ${run_id} attempt ${attempt} failed" >> results/failures.log
  done
  echo "Run ${run_id} failed 3 times; stopping. See results/failures.log." >&2
  exit 1
}

# Repetitions are the outer loop, so slow drift on the host spreads across all cells.
for rep in $(seq 1 "$REPS"); do
  # H1: the runtime default follows the CPU limit (healthy scenario only).
  run_once runner-1cpu "$rep" --strategy C1 --scenario healthy
  run_once runner-2cpu "$rep" --strategy C1 --scenario healthy

  # H2, H3 and the gate: same CPU limit, different ways of bounding concurrency.
  for scenario in healthy enrichment-degraded records-degraded; do
    for strategy in C2 C3 C4; do
      run_once runner-1cpu "$rep" --strategy "$strategy" --scenario "$scenario"
    done
  done
done

docker compose --profile runner down
# Written to a temp file first, so a failed summary leaves no summary.md behind.
dotnet run --project src/Lab.Summary -c Release --no-build -- results/results.csv > results/summary.md.tmp
mv results/summary.md.tmp results/summary.md
echo "Done. See results/summary.md"
