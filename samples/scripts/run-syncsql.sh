#!/usr/bin/env bash
#
# Runs the whole syncsql pipeline against the sample fleet that
# samples/scripts/setup-databases.sh brought up, writing everything under
# samples/output/.
#
#   samples/scripts/run-syncsql.sh
#   samples/scripts/run-syncsql.sh --server-include '^SAMPLES-ORACLE$'
#   samples/scripts/run-syncsql.sh --output-root /tmp/syncsql-samples
#
# The four steps are the same ones a real pipeline runs, in the same order:
#
#   validate-config -> sync -> metrics update -> catalog build -> lint
#
# No git is involved: `sync` only writes local files, and `catalog build` runs
# without --repo-root, so the history/heatmap parts of catalog.json stay empty.

# shellcheck source=lib/common.sh
source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/lib/common.sh"

CONFIG="$SAMPLES_DIR/config/servers.samples.json"
OUTPUT_ROOT="$SAMPLES_DIR/output"
SERVER_INCLUDE=""
SERVER_EXCLUDE=""
SKIP_LINT=0
CLEAN=0
BUILD=1

usage() {
  cat <<'USAGE'
Usage: run-syncsql.sh [options]

  --config <path>          SyncSQL config. Default: samples/config/servers.samples.json
  --output-root <path>     Where the object tree, metrics and catalog.json go.
                           Default: samples/output
  --server-include <regex> Only extract servers matching this. Repeatable.
  --server-exclude <regex> Skip servers matching this. Repeatable.
  --clean                  Delete the output root before extracting.
  --skip-lint              Do not run the T-SQL lint pass at the end.
  --no-build               Assume the CLI is already built (skips dotnet build).
  -h, --help               This text.
USAGE
}

INCLUDE_ARGS=(); EXCLUDE_ARGS=()
while (($# > 0)); do
  case "$1" in
    --config) CONFIG="${2:?--config needs a path}"; shift 2 ;;
    --output-root) OUTPUT_ROOT="${2:?--output-root needs a path}"; shift 2 ;;
    --server-include) INCLUDE_ARGS+=(--server-include "${2:?--server-include needs a regex}"); shift 2 ;;
    --server-exclude) EXCLUDE_ARGS+=(--server-exclude "${2:?--server-exclude needs a regex}"); shift 2 ;;
    --clean) CLEAN=1; shift ;;
    --skip-lint) SKIP_LINT=1; shift ;;
    --no-build) BUILD=0; shift ;;
    -h|--help) usage; exit 0 ;;
    *) die "Unknown option '$1'. Try --help." ;;
  esac
done

need_cmd dotnet "Install the .NET SDK pinned by global.json: https://dotnet.microsoft.com/download"
# jq writes the credentials file below and reads the server names out of the config.
need_cmd jq "Install it with 'apt install jq', 'brew install jq' or 'winget install jqlang.jq'."
load_env

[[ -f "$CONFIG" ]] || die "Config not found: $CONFIG"

# Credentials never go on the command line (other processes can read argv) and
# never into the config. This file is written 0600 under the git-ignored cache.
CREDENTIALS="$CACHE_DIR/credentials.json"
mkdir -p "$CACHE_DIR"
umask 177
jq -n \
  --arg mssqlPassword "$MSSQL_SA_PASSWORD" \
  --arg oraclePassword "$ORACLE_PASSWORD" \
  '{
     SAMPLES_MSSQL:  { user: "sa",     password: $mssqlPassword  },
     SAMPLES_ORACLE: { user: "SYSTEM", password: $oraclePassword }
   }' > "$CREDENTIALS"
umask 022

if ((CLEAN)) && [[ -d "$OUTPUT_ROOT" ]]; then
  log "Clearing $OUTPUT_ROOT"
  rm -rf "$OUTPUT_ROOT"
fi
mkdir -p "$OUTPUT_ROOT"

CLI_PROJECT="$REPO_DIR/cli/src/SyncSql.Cli"
if ((BUILD)); then
  log "Building the CLI"
  dotnet build "$CLI_PROJECT" -c Release --nologo --verbosity quiet
fi

# No --nologo here: `dotnet run` does not define it, so it would be forwarded
# to syncsql as an argument and rejected there.
syncsql() { dotnet run --project "$CLI_PROJECT" -c Release --no-build -- "$@"; }

log "validate-config"
syncsql validate-config --config "$CONFIG"

log "sync"
syncsql sync \
  --config "$CONFIG" \
  --credentials-file "$CREDENTIALS" \
  --output-root "$OUTPUT_ROOT" \
  ${INCLUDE_ARGS[@]+"${INCLUDE_ARGS[@]}"} ${EXCLUDE_ARGS[@]+"${EXCLUDE_ARGS[@]}"}

log "metrics update"
syncsql metrics update --output-root "$OUTPUT_ROOT"

log "catalog build"
syncsql catalog build \
  --output-root "$OUTPUT_ROOT" \
  --metrics-root "$OUTPUT_ROOT/metrics"

# `lint` is the T-SQL parser, and only the T-SQL parser. Handed the shared output
# root it would walk into SAMPLES-ORACLE too and feed PL/SQL to ScriptDom, so it
# gets one --path per MSSQL server instead - read from the config, so a renamed or
# added server stays covered.
MSSQL_TREES=()
while IFS= read -r server; do
  if [[ -n "$server" && -d "$OUTPUT_ROOT/$server" ]]; then MSSQL_TREES+=(--path "$OUTPUT_ROOT/$server"); fi
done < <(jq -r '.servers[] | select(.type == "mssql") | .name' "$CONFIG")

if ((SKIP_LINT)); then
  step "lint skipped (--skip-lint)"
elif ((${#MSSQL_TREES[@]} == 0)); then
  step "lint skipped (nothing was extracted from an MSSQL server)"
else
  log "lint"
  # Findings are informational here: these are third-party sample scripts, and
  # SELECT */NOLOCK/cursor hits in them are exactly what the demo is meant to
  # show. Only a parse error should be loud.
  syncsql lint "${MSSQL_TREES[@]}" --fail-on error
fi

log "Done"
OBJECTS="$(find "$OUTPUT_ROOT" -name '*.sql' -type f | wc -l | tr -d ' ')"
cat <<EOF
    objects extracted : $OBJECTS
    catalog           : $OUTPUT_ROOT/catalog.json
    metrics history   : $OUTPUT_ROOT/metrics

    To browse it: copy $OUTPUT_ROOT/catalog.json into site/public/ and run 'npm run dev' in site/.
EOF
