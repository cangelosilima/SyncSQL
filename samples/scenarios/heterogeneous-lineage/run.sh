#!/usr/bin/env bash
set -euo pipefail
scenario="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$scenario/../../.." && pwd)"
project="$repo/cli/tests/SyncSql.Samples.Benchmark.Tests"
gateway=0
case "${1:-}" in
  --offline) dotnet test "$project" --filter 'FullyQualifiedName~HeterogeneousLineageTests'; exit ;;
  --down) docker compose --env-file "$scenario/.env.example" -f "$scenario/compose.yml" -f "$scenario/compose.gateway.yml" down; exit ;;
  --gateway) gateway=1 ;;
  "") ;;
  *) echo "Usage: $0 [--offline|--down|--gateway]" >&2; exit 2 ;;
esac
[[ -f "$scenario/.env" ]] || cp "$scenario/.env.example" "$scenario/.env"
export BENCH_PASSWORD
BENCH_PASSWORD="$(sed -n 's/^BENCH_PASSWORD=//p' "$scenario/.env" | head -n 1 | tr -d '\r')"
compose=(docker compose --env-file "$scenario/.env" -f "$scenario/compose.yml")
if ((gateway)); then
  compose+=(-f "$scenario/compose.gateway.yml")
  if [[ -n "${ORACLE_GATEWAY_IMAGE:-}" ]]; then
    docker image inspect "$ORACLE_GATEWAY_IMAGE" --format '{{.Id}}' || { echo 'Pull or build ORACLE_GATEWAY_IMAGE first.' >&2; exit 1; }
  else
    media="$scenario/gateway/.cache/LINUX.X64_193000_gateways.zip"
    [[ -f "$media" ]] || { echo "Oracle gateway installer missing: $media. See gateway/README.md or set ORACLE_GATEWAY_IMAGE." >&2; exit 1; }
    "${compose[@]}" build gateway
  fi
fi
"${compose[@]}" up -d --no-build --wait --wait-timeout 900
if ((gateway)); then
  "${compose[@]}" exec -T helios bash /opt/syncsql/gateway/configure.sh
fi
dotnet publish "$repo/cli/src/SyncSql.Cli" -c Release -o "$scenario/.cache/cli" --verbosity quiet
export SYNCSQL_CLI="$scenario/.cache/cli/SyncSql.Cli.dll"
export SYNCSQL_HETEROGENEOUS=1
export SYNCSQL_HETEROGENEOUS_GATEWAY="$gateway"
dotnet test "$project" -c Release --filter 'FullyQualifiedName~Heterogeneous' --logger 'console;verbosity=normal' --logger 'trx;LogFileName=heterogeneous.trx'
