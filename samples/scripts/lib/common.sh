#!/usr/bin/env bash
# Shared plumbing for samples/scripts/*.sh. Sourced, never executed.

set -o errexit
set -o nounset
set -o pipefail

SAMPLES_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
REPO_DIR="$(cd -- "$SAMPLES_DIR/.." && pwd)"
CACHE_DIR="$SAMPLES_DIR/.cache"
SOURCES_DIR="$CACHE_DIR/sources"
BACKUPS_DIR="$CACHE_DIR/backups"
WORK_DIR="$CACHE_DIR/work"
MANIFEST="$SAMPLES_DIR/samples.json"
COMPOSE_FILE="$SAMPLES_DIR/docker/docker-compose.yml"
ENV_FILE="$SAMPLES_DIR/.env"

if [[ -t 1 && -z "${NO_COLOR:-}" ]]; then
  C_RESET=$'\033[0m'; C_BOLD=$'\033[1m'; C_DIM=$'\033[2m'
  C_RED=$'\033[31m'; C_GREEN=$'\033[32m'; C_YELLOW=$'\033[33m'; C_BLUE=$'\033[34m'
else
  C_RESET=''; C_BOLD=''; C_DIM=''; C_RED=''; C_GREEN=''; C_YELLOW=''; C_BLUE=''
fi

log()   { printf '%s\n' "${C_BLUE}==>${C_RESET} ${C_BOLD}$*${C_RESET}"; }
step()  { printf '%s\n' "    ${C_DIM}-${C_RESET} $*"; }
ok()    { printf '%s\n' "    ${C_GREEN}OK${C_RESET} $*"; }
warn()  { printf '%s\n' "    ${C_YELLOW}!!${C_RESET} $*" >&2; }
die()   { printf '%s\n' "${C_RED}error:${C_RESET} $*" >&2; exit 1; }

need_cmd() {
  local cmd="$1" hint="${2:-}"
  command -v "$cmd" >/dev/null 2>&1 || die "'$cmd' is required but not on PATH.${hint:+ $hint}"
}

require_prerequisites() {
  need_cmd docker "Install Docker Desktop or Docker Engine: https://docs.docker.com/get-docker/"
  need_cmd jq "Install it with 'apt install jq', 'brew install jq' or 'winget install jqlang.jq'."
  docker compose version >/dev/null 2>&1 \
    || die "The Docker Compose v2 plugin is required ('docker compose version' failed)."
  docker info >/dev/null 2>&1 \
    || die "The Docker daemon is not reachable. Start Docker and try again."
}

# samples/.env holds the container passwords and host ports. Created from the
# checked-in example on first run so nobody has to remember the copy step.
load_env() {
  if [[ ! -f "$ENV_FILE" ]]; then
    cp "$SAMPLES_DIR/docker/.env.example" "$ENV_FILE"
    warn "Created $ENV_FILE from docker/.env.example - edit it if you want different passwords or ports."
  fi
  set -o allexport
  # shellcheck disable=SC1090
  source "$ENV_FILE"
  set +o allexport

  : "${MSSQL_SA_PASSWORD:?MSSQL_SA_PASSWORD is not set in samples/.env}"
  : "${ORACLE_PASSWORD:?ORACLE_PASSWORD is not set in samples/.env}"
  : "${ORACLE_SAMPLE_PASSWORD:=$ORACLE_PASSWORD}"
  : "${MSSQL_PORT:=14330}"
  : "${ORACLE_PORT:=15210}"
  export MSSQL_SA_PASSWORD ORACLE_PASSWORD ORACLE_SAMPLE_PASSWORD MSSQL_PORT ORACLE_PORT
}

compose() { docker compose --project-directory "$SAMPLES_DIR/docker" -f "$COMPOSE_FILE" --env-file "$ENV_FILE" "$@"; }

# --- manifest ---------------------------------------------------------------

manifest() { jq -r "$1" "$MANIFEST"; }

# Sample ids in provisioning order, filtered by engine/tier/only/skip.
select_samples() {
  local engine="$1" tier="$2" only="$3" skip="$4"
  jq -r \
    --arg engine "$engine" --arg tier "$tier" --arg only "$only" --arg skip "$skip" '
    ($only  | split(",") | map(select(length > 0))) as $only  |
    ($skip  | split(",") | map(select(length > 0))) as $skip  |
    ($tier  | if . == "all" then ["standard","heavy"] else [.] end) as $tiers |
    .samples
    | sort_by(.order)
    | map(select($engine == "all" or .engine == $engine))
    | map(select(.provision.type != "none"))
    | map(select(($only | length) == 0 or (.id | IN($only[]))))
    | map(select((.id | IN($skip[])) | not))
    | map(select(($only | length) > 0 or (.tier | IN($tiers[]))))
    | .[].id' "$MANIFEST"
}

sample_field() { jq -r --arg id "$1" ".samples[] | select(.id == \$id) | $2" "$MANIFEST"; }

# --- sources ----------------------------------------------------------------

# One shallow, sparse checkout per upstream repository. Re-running fetches
# updates instead of re-cloning.
fetch_source() {
  local name="$1"
  local repo ref mode dest
  repo="$(manifest ".sources[\"$name\"].repo")"
  ref="$(manifest ".sources[\"$name\"].ref")"
  mode="$(manifest ".sources[\"$name\"].sparseMode // \"cone\"")"
  dest="$SOURCES_DIR/$name"

  local -a sparse=()
  while IFS= read -r line; do sparse+=("$line"); done < <(manifest ".sources[\"$name\"].sparsePaths[]?")

  local -a clone_args=(--quiet --depth 1 --branch "$ref" --filter=tree:0)
  if ((${#sparse[@]} > 0)); then clone_args+=(--sparse); fi

  if [[ -d "$dest/.git" ]]; then
    step "updating $name"
    git -C "$dest" fetch --quiet --depth 1 origin "$ref"
    git -C "$dest" checkout --quiet --detach FETCH_HEAD
  else
    step "cloning $name ($repo)"
    mkdir -p "$(dirname "$dest")"
    # --filter=tree:0 keeps the initial transfer down to the commit itself; the
    # sparse patterns below decide what actually gets materialised. Without
    # both, sql-server-samples alone is a multi-gigabyte checkout.
    git clone "${clone_args[@]}" "$repo" "$dest"
  fi

  if ((${#sparse[@]} > 0)); then
    if [[ "$mode" == "no-cone" ]]; then
      git -C "$dest" sparse-checkout set --no-cone "${sparse[@]}" >/dev/null
    else
      git -C "$dest" sparse-checkout set --cone "${sparse[@]}" >/dev/null
    fi
    git -C "$dest" checkout --quiet
  fi
  ok "$name at $(git -C "$dest" rev-parse --short HEAD)"
}

# Backups are large and immutable, so an existing file of the right size is kept.
download_backup() {
  local name="$1" url size dest
  url="$(manifest ".downloads[\"$name\"].url")"
  size="$(manifest ".downloads[\"$name\"].sizeBytes")"
  dest="$BACKUPS_DIR/$name"

  if [[ -f "$dest" ]]; then
    local actual
    actual="$(stat -c '%s' "$dest" 2>/dev/null || stat -f '%z' "$dest")"
    if [[ "$actual" == "$size" ]]; then
      ok "$name already downloaded"
      return 0
    fi
    warn "$name is $actual bytes, expected $size - re-downloading"
  fi

  mkdir -p "$BACKUPS_DIR"
  step "downloading $name ($((size / 1024 / 1024)) MB)"
  curl --fail --location --progress-bar --output "$dest.partial" "$url"
  mv "$dest.partial" "$dest"
  ok "$name"
}

# --- SQL Server -------------------------------------------------------------

SQLCMD=/opt/mssql-tools/bin/sqlcmd

# Runs one .sql file that already lives under samples/ (mounted read-only at
# /samples in the tools container). $1 is a path relative to samples/.
#   -b  stop and fail on the first error
#   -I  QUOTED_IDENTIFIER ON, which temporal tables and filtered indexes need
#   -f 65001  read the file as UTF-8 (the IVS demo is full of Japanese text)
# The password comes from SQLCMDPASSWORD in the container's environment, so it
# never appears in a process listing.
mssql_run_file() {
  local relative="$1" database="${2:-master}"
  compose run --rm -T mssql-tools -c \
    "$SQLCMD -S mssql,1433 -U sa -d '$database' -b -I -f 65001 -i '/samples/$relative'"
}

# Same, for SQL this script generated into samples/.cache/work/.
mssql_run_sql() {
  local sql="$1" database="${2:-master}" name="${3:-generated}"
  mkdir -p "$WORK_DIR"
  printf '%s\n' "$sql" > "$WORK_DIR/$name.sql"
  mssql_run_file ".cache/work/$name.sql" "$database"
}

# Returns pipe-separated rows with no headers, for parsing.
mssql_query() {
  local sql="$1" name="${2:-query}"
  mkdir -p "$WORK_DIR"
  printf 'SET NOCOUNT ON;\n%s\n' "$sql" > "$WORK_DIR/$name.sql"
  compose run --rm -T mssql-tools -c \
    "$SQLCMD -S mssql,1433 -U sa -b -I -h -1 -W -s '|' -i '/samples/.cache/work/$name.sql'"
}

wait_for_mssql() {
  local attempts="${1:-60}"
  step "waiting for SQL Server on port $MSSQL_PORT"
  for ((i = 1; i <= attempts; i++)); do
    if mssql_query "SELECT 1;" readiness >/dev/null 2>&1; then
      ok "SQL Server is accepting queries"
      return 0
    fi
    sleep 5
  done
  die "SQL Server did not become ready after $((attempts * 5))s. Try 'docker compose -f $COMPOSE_FILE logs mssql'."
}

mssql_create_database() {
  local database="$1"
  mssql_run_sql "IF DB_ID(N'$database') IS NULL CREATE DATABASE [$database];" master "create-db"
}

# Restores a backup without assuming anything about the paths it was taken from:
# RESTORE FILELISTONLY reports the logical files, and every one of them gets a
# MOVE to this container's data directory. Type D is a data file, L a log file,
# and S a filestream / memory-optimized container, which moves to a directory
# rather than a file.
mssql_restore_backup() {
  local backup="$1" database="$2"
  local disk="/var/opt/mssql/backup/$backup"

  step "reading file list from $backup"
  local filelist move=""
  filelist="$(mssql_query "RESTORE FILELISTONLY FROM DISK = N'$disk';" filelistonly)"

  local logical type target sanitized
  while IFS='|' read -r logical _physical type _rest; do
    if [[ "$logical" == *"rows affected"* ]]; then continue; fi
    logical="${logical%"${logical##*[![:space:]]}"}"
    type="${type//[[:space:]]/}"
    if [[ -z "$logical" || -z "$type" ]]; then continue; fi

    sanitized="${logical//[^A-Za-z0-9_.-]/_}"
    case "$type" in
      D) target="/var/opt/mssql/data/${database}__${sanitized}.mdf" ;;
      L) target="/var/opt/mssql/data/${database}__${sanitized}.ldf" ;;
      S) target="/var/opt/mssql/data/${database}__${sanitized}" ;;
      *) warn "unknown backup file type '$type' for '$logical' - skipping its MOVE"; continue ;;
    esac
    move+=", MOVE N'$logical' TO N'$target'"
  done <<< "$filelist"

  [[ -n "$move" ]] || die "RESTORE FILELISTONLY returned no usable rows for $backup."

  step "restoring $backup as $database"
  mssql_run_sql "
IF DB_ID(N'$database') IS NOT NULL
BEGIN
    ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
END
RESTORE DATABASE [$database] FROM DISK = N'$disk'
WITH REPLACE, RECOVERY, STATS = 10${move};
ALTER DATABASE [$database] SET MULTI_USER;
-- The backups predate this container's engine version; without this the
-- restored database keeps an old compatibility level and some demo syntax
-- (and SyncSQL's own catalog queries) behave differently than expected.
DECLARE @level tinyint = (SELECT CAST(SERVERPROPERTY('ProductMajorVersion') AS int) * 10);
EXEC('ALTER DATABASE [$database] SET COMPATIBILITY_LEVEL = ' + @level);
" master "restore-$database"
}

# --- Oracle -----------------------------------------------------------------

wait_for_oracle() {
  local attempts="${1:-90}"
  step "waiting for Oracle on port $ORACLE_PORT"
  for ((i = 1; i <= attempts; i++)); do
    if [[ "$(docker inspect --format '{{.State.Health.Status}}' syncsql-samples-oracle 2>/dev/null || echo none)" == "healthy" ]]; then
      ok "Oracle is healthy"
      return 0
    fi
    sleep 5
  done
  die "Oracle did not become healthy after $((attempts * 5))s. Try 'docker compose -f $COMPOSE_FILE logs oracle'."
}

# Runs one of the upstream <schema>_install.sql drivers. Those scripts are
# SQL*Plus programs - @@ includes, SPOOL, ACCEPT prompts - so they run through
# sqlplus inside the container rather than through a driver.
#
# -L makes sqlplus attempt the connection exactly once and take the password
# from stdin, which keeps it out of the container's process list. The four
# answers below are, in order: SYSTEM's password, the new schema's password,
# the tablespace (blank accepts the PDB default) and whether to overwrite an
# existing schema.
#
# --workdir /tmp because the install scripts SPOOL a log into the working
# directory, and /samples is mounted read-only.
oracle_run_install_script() {
  local workdir="$1" script="$2"
  local path="/samples/.cache/sources/db-sample-schemas/$workdir/$script"
  printf '%s\n%s\n\n%s\n' "$ORACLE_PASSWORD" "$ORACLE_SAMPLE_PASSWORD" "YES" \
    | compose exec -T --workdir /tmp oracle \
        sqlplus -s -L "system@localhost:1521/FREEPDB1" "@$path"
}
