#!/usr/bin/env bash
#
# Runs the sample-fleet benchmark: extracts every sample database, builds the
# catalog, and asserts the result against samples/expected/.
#
#   samples/scripts/run-benchmark.sh                  # extract, then assert
#   samples/scripts/run-benchmark.sh --reuse          # assert against samples/output as it stands
#   samples/scripts/run-benchmark.sh --update-baseline
#
# The databases have to be up first: samples/scripts/setup-databases.sh.

# shellcheck source=lib/common.sh
source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/lib/common.sh"

REUSE=0
UPDATE_BASELINE=0
FILTER=""

usage() {
  cat <<'USAGE'
Usage: run-benchmark.sh [options]

  --reuse              Assert against the existing samples/output instead of re-extracting.
  --update-baseline    Re-record samples/expected/baseline.json from this run.
  --filter <expr>      Passed through to `dotnet test --filter`.
  -h, --help           This text.
USAGE
}

while (($# > 0)); do
  case "$1" in
    --reuse) REUSE=1; shift ;;
    --update-baseline) UPDATE_BASELINE=1; shift ;;
    --filter) FILTER="${2:?--filter needs an expression}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) die "Unknown option '$1'. Try --help." ;;
  esac
done

need_cmd dotnet "Install the .NET SDK pinned by global.json: https://dotnet.microsoft.com/download"
load_env

export SYNCSQL_SAMPLES=1
if ((REUSE)); then export SYNCSQL_SAMPLES_REUSE=1; fi
if ((UPDATE_BASELINE)); then export SYNCSQL_SAMPLES_UPDATE_BASELINE=1; fi

# Publishing the CLI once and pointing the benchmark at it keeps `dotnet run`
# (and its implicit restore) out of the middle of the test run.
PUBLISH_DIR="$CACHE_DIR/cli"
log "Publishing the CLI to $PUBLISH_DIR"
dotnet publish "$REPO_DIR/cli/src/SyncSql.Cli" -c Release -o "$PUBLISH_DIR" --nologo --verbosity quiet
export SYNCSQL_CLI="$PUBLISH_DIR/SyncSql.Cli.dll"

log "Running the benchmark"
TEST_ARGS=(test "$REPO_DIR/cli/tests/SyncSql.Samples.Benchmark.Tests" -c Release --nologo)
if [[ -n "$FILTER" ]]; then TEST_ARGS+=(--filter "$FILTER"); fi
dotnet "${TEST_ARGS[@]}"
