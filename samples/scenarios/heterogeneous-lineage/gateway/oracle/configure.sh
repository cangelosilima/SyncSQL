#!/usr/bin/env bash
set -euo pipefail
tns="$ORACLE_HOME/network/admin/tnsnames.ora"
touch "$tns"
temporary="$(mktemp)"
trap 'rm -f "$temporary"' EXIT
sed '/^# BEGIN SYNCSQL GATEWAYS$/,/^# END SYNCSQL GATEWAYS$/d' "$tns" > "$temporary"
cat /opt/syncsql/gateway/tnsnames.ora >> "$temporary"
cat "$temporary" > "$tns"

# The stock image tries EZCONNECT first. Our aliases intentionally match SQL
# hostnames, so that order resolves ATLAS_SQL directly on port 1521 and bypasses
# the gateway. Preserve the other network settings but prefer TNSNAMES.
sqlnet="$ORACLE_HOME/network/admin/sqlnet.ora"
touch "$sqlnet"
if grep -qiE '^[[:space:]]*NAMES\.DIRECTORY_PATH[[:space:]]*=' "$sqlnet"; then
  sed -i 's/^[[:space:]]*NAMES\.DIRECTORY_PATH[[:space:]]*=.*/NAMES.DIRECTORY_PATH = (TNSNAMES, EZCONNECT)/I' "$sqlnet"
else
  printf '\nNAMES.DIRECTORY_PATH = (TNSNAMES, EZCONNECT)\n' >> "$sqlnet"
fi
