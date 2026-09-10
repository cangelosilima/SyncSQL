#!/usr/bin/env bash
#
# Brings up the sample database fleet: one SQL Server and one Oracle container,
# loaded with the samples described in samples/samples.json.
#
#   samples/scripts/setup-databases.sh              # everything in the standard tier
#   samples/scripts/setup-databases.sh --tier all   # + the slow/large ones
#   samples/scripts/setup-databases.sh --only human-resources,sql-graph
#   samples/scripts/setup-databases.sh --status
#   samples/scripts/setup-databases.sh --down
#
# Re-running is safe: sources are updated rather than re-cloned, backups already
# downloaded are kept, and every sample either restores over itself or drops what
# it is about to create.

# shellcheck source=lib/common.sh
source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/lib/common.sh"

ENGINE=all
TIER=standard
ONLY=""
SKIP=""
SKIP_FETCH=0
ACTION=provision

usage() {
  sed -n '2,20p' "${BASH_SOURCE[0]}" | sed 's/^#\s\?//'
  cat <<'USAGE'

Options:
  --engine mssql|oracle|all   Which engine to provision. Default: all.
  --tier standard|heavy|all   Which tier to install. Default: standard.
                              'heavy' covers the samples that need a very large
                              download or hours of row-by-row inserts.
  --only  <id>[,<id>...]      Provision these sample ids and their prerequisites (ignores --tier).
  --skip  <id>[,<id>...]      Skip these sample ids.
  --skip-fetch                Reuse samples/.cache as-is; do not fetch or download.
  --status                    Report container and sample state, then exit.
  --down                      Stop the containers and delete their volumes, then exit.
  -h, --help                  This text.

Sample ids come from samples/samples.json; --status lists them.
USAGE
}

while (($# > 0)); do
  case "$1" in
    --engine) ENGINE="${2:?--engine needs a value}"; shift 2 ;;
    --tier)   TIER="${2:?--tier needs a value}"; shift 2 ;;
    --only)   ONLY="${2:?--only needs a value}"; shift 2 ;;
    --skip)   SKIP="${2:?--skip needs a value}"; shift 2 ;;
    --skip-fetch) SKIP_FETCH=1; shift ;;
    --status) ACTION=status; shift ;;
    --down)   ACTION=down; shift ;;
    -h|--help) usage; exit 0 ;;
    *) die "Unknown option '$1'. Try --help." ;;
  esac
done

case "$ENGINE" in mssql|oracle|all) ;; *) die "--engine must be mssql, oracle or all." ;; esac
case "$TIER" in standard|heavy|all) ;; *) die "--tier must be standard, heavy or all." ;; esac

require_prerequisites
load_env

# --- --down -----------------------------------------------------------------

if [[ "$ACTION" == down ]]; then
  log "Tearing down the sample fleet"
  compose down --volumes --remove-orphans
  ok "Containers and volumes removed. samples/.cache is untouched - delete it by hand to reclaim the downloads."
  exit 0
fi

# --- --status ---------------------------------------------------------------

if [[ "$ACTION" == status ]]; then
  log "Containers"
  compose ps || true
  log "Samples"
  jq -r '
    .samples
    | sort_by(.order)
    | .[]
    | "  \(.engine | if . == "mssql" then "mssql " else "oracle" end)  \(.tier | .[0:11] + (" " * (11 - length)))  \(.id)"
  ' "$MANIFEST"
  exit 0
fi

# --- fetch ------------------------------------------------------------------

SELECTED=()
while IFS= read -r line; do SELECTED+=("$line"); done < <(select_samples "$ENGINE" "$TIER" "$ONLY" "$SKIP")
((${#SELECTED[@]} > 0)) || die "No samples matched --engine $ENGINE --tier $TIER${ONLY:+ --only $ONLY}${SKIP:+ --skip $SKIP}."

log "Provisioning ${#SELECTED[@]} sample(s): ${SELECTED[*]}"

# The selection above pulls in each sample's declared prerequisites, so this only
# fires when --skip removed one on purpose - in which case the dependent sample is
# about to install against a base database nobody restored.
while IFS= read -r unmet; do
  if [[ -n "$unmet" ]]; then warn "$unmet, which --skip excluded - it will install against whatever is already there"; fi
done < <(skipped_requirements "$ENGINE" "$TIER" "$ONLY" "$SKIP")

# Which engines the selection actually needs, so an --engine oracle run never
# waits on SQL Server (or downloads a single backup).
NEEDS_MSSQL=0; NEEDS_ORACLE=0
for id in "${SELECTED[@]}"; do
  case "$(sample_field "$id" .engine)" in
    mssql)  NEEDS_MSSQL=1 ;;
    oracle) NEEDS_ORACLE=1 ;;
  esac
done

mkdir -p "$SOURCES_DIR" "$BACKUPS_DIR" "$WORK_DIR"

if ((SKIP_FETCH)); then
  log "Skipping fetch (--skip-fetch)"
else
  log "Fetching upstream sources"
  need_cmd git
  if ((NEEDS_MSSQL));  then fetch_source sql-server-samples; fi
  if ((NEEDS_ORACLE)); then fetch_source db-sample-schemas; fi

  if ((NEEDS_MSSQL)); then
    log "Downloading sample database backups"
    need_cmd curl
    for id in "${SELECTED[@]}"; do
      [[ "$(sample_field "$id" .provision.type)" == "mssql-restore" ]] || continue
      while IFS= read -r backup; do
        if [[ -n "$backup" ]]; then download_backup "$backup"; fi
      done < <(sample_field "$id" '.provision.restores[].backup')
    done
  fi
fi

# --- containers -------------------------------------------------------------

log "Starting containers"
SERVICES=()
if ((NEEDS_MSSQL));  then SERVICES+=(mssql);  fi
if ((NEEDS_ORACLE)); then SERVICES+=(oracle); fi
compose up -d "${SERVICES[@]}"

if ((NEEDS_MSSQL));  then wait_for_mssql;  fi
if ((NEEDS_ORACLE)); then wait_for_oracle; fi

# --- provision --------------------------------------------------------------

INSTALLED=(); FAILED=()

provision_mssql_restore() {
  local id="$1" backup database
  while IFS=$'\t' read -r backup database; do
    [[ -n "$backup" ]] || continue
    mssql_restore_backup "$backup" "$database"
  done < <(sample_field "$id" '.provision.restores[] | "\(.backup)\t\(.database)"')
}

provision_mssql_scripts() {
  local id="$1" database from path relative
  database="$(sample_field "$id" .provision.database)"
  mssql_create_database "$database"
  while IFS=$'\t' read -r from path; do
    [[ -n "$path" ]] || continue
    if [[ "$from" == local ]]; then
      relative="$(sample_field "$id" .engine)/$id/$path"
    else
      relative=".cache/sources/sql-server-samples/$(sample_field "$id" .upstream.path)/$path"
    fi
    step "$path"
    mssql_run_file "$relative" "$database"
  done < <(sample_field "$id" '.provision.scripts[] | "\(.from)\t\(.path)"')
}

provision_oracle() {
  local id="$1" workdir script
  workdir="$(sample_field "$id" .provision.workdir)"
  script="$(sample_field "$id" .provision.script)"
  step "$workdir/$script"
  oracle_run_install_script "$workdir" "$script"
}

for id in "${SELECTED[@]}"; do
  type="$(sample_field "$id" .provision.type)"
  log "$id ($(sample_field "$id" .title))"
  if (
    case "$type" in
      mssql-restore) provision_mssql_restore "$id" ;;
      mssql-scripts) provision_mssql_scripts "$id" ;;
      oracle-install-script) provision_oracle "$id" ;;
      *) die "Sample '$id' has unsupported provision type '$type'." ;;
    esac
  ); then
    ok "$id installed"
    INSTALLED+=("$id")
  else
    warn "$id failed - continuing with the rest"
    FAILED+=("$id")
  fi
done

# --- summary ----------------------------------------------------------------

log "Done"
printf '    installed: %s\n' "${INSTALLED[*]:-(none)}"
if ((${#FAILED[@]} > 0)); then
  printf '    %sfailed:%s    %s\n' "$C_RED" "$C_RESET" "${FAILED[*]}"
fi
cat <<EOF

    SQL Server  localhost,${MSSQL_PORT}   user 'sa'      (password: samples/.env MSSQL_SA_PASSWORD)
    Oracle      localhost:${ORACLE_PORT}/FREEPDB1  user 'SYSTEM'  (password: samples/.env ORACLE_PASSWORD)

    Next: samples/scripts/run-syncsql.sh
EOF

((${#FAILED[@]} == 0)) || exit 1
