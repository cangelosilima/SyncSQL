#!/usr/bin/env bash
set -euo pipefail
cd /opt/install
# Oracle's published checksum. Reject an HTML sign-in page or altered archive.
echo '668bc29afe4c75e80c357d882b32bcc23407c11aef7bae3122dbb9783661dbd1  gateways.zip' | sha256sum -c -
unzip -q gateways.zip
response="$(find /opt/install -name tg.rsp -print -quit)"
[[ -n "$response" ]] || { echo 'Oracle media is missing response/tg.rsp.' >&2; exit 1; }
# The response template lists component names without versions. Read the exact
# version from the SQL Server component shipped in this verified archive.
component_dir="$(find /opt/install -type d -name oracle.rdbms.tg4msql -print -quit)"
[[ -n "$component_dir" ]] || { echo 'Oracle media is missing the SQL Server gateway component.' >&2; exit 1; }
version_dir="$(find "$component_dir" -mindepth 1 -maxdepth 1 -type d -print -quit)"
component="oracle.rdbms.tg4msql:$(basename "$version_dir")"
sed -i \
  -e 's|^UNIX_GROUP_NAME=.*|UNIX_GROUP_NAME=oinstall|' \
  -e 's|^INVENTORY_LOCATION=.*|INVENTORY_LOCATION=/opt/oracle/oraInventory|' \
  -e "s|^ORACLE_HOME=.*|ORACLE_HOME=$ORACLE_HOME|" \
  -e "s|^ORACLE_BASE=.*|ORACLE_BASE=$ORACLE_BASE|" \
  -e "s|^oracle.install.tg.customComponents=.*|oracle.install.tg.customComponents={$component}|" \
  -e 's|^oracle.install.tg.msqlConStr=.*|oracle.install.tg.msqlConStr={ATLAS_SQL,1433,MSSQLSERVER,Commerce}|' \
  "$response"
installer="$(find /opt/install -name runInstaller -print -quit)"
set +e
# Container swap/kernel checks do not describe the installed userspace. Runtime
# binaries, listener health, and the benchmark's real queries remain mandatory.
"$installer" -silent -noconfig -ignorePrereqFailure -waitforcompletion -responseFile "$response"
result=$?
set -e
# OUI returns 6 for a successful install with warnings (e.g. container swap).
[[ "$result" == 0 || "$result" == 6 ]] || exit "$result"
test -x "$ORACLE_HOME/bin/dg4msql"
test -x "$ORACLE_HOME/bin/lsnrctl"
