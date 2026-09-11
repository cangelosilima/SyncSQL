#!/usr/bin/env bash
set -euo pipefail
: "${ORACLE_HOME:?The gateway image must define ORACLE_HOME}"
test -x "$ORACLE_HOME/bin/dg4msql"
test -x "$ORACLE_HOME/bin/lsnrctl"
export LD_LIBRARY_PATH="$ORACLE_HOME/lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
mkdir -p "$ORACLE_HOME/network/admin" "$ORACLE_HOME/dg4msql/admin"
cp /opt/syncsql/network/listener.ora "$ORACLE_HOME/network/admin/listener.ora"
cp /opt/syncsql/network/init*.ora "$ORACLE_HOME/dg4msql/admin/"
export TNS_ADMIN="$ORACLE_HOME/network/admin"
trap '"$ORACLE_HOME/bin/lsnrctl" stop LISTENER; exit 0' TERM INT
"$ORACLE_HOME/bin/lsnrctl" start LISTENER
while "$ORACLE_HOME/bin/lsnrctl" status LISTENER >/dev/null; do
  sleep 5 & wait $!
done
exit 1
