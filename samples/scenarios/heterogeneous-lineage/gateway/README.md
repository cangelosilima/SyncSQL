# Oracle Database Gateway for SQL Server

This optional Docker overlay runs **dg4msql**, Oracle's SQL Server gateway. It
adds executable Oracle outbound reads to the heterogeneous lineage benchmark.
It does not supply an Oracle linked-server provider for Linux SQL Server.

## Install and run

Download **LINUX.X64_193000_gateways.zip** from the
[Oracle 19c Linux downloads page](https://www.oracle.com/database/technologies/oracle19c-linux-downloads.html).
Oracle requires sign-in and acceptance of its download terms. The repository
does not redistribute the installer or automate account sign-in.

Place the archive in `gateway/.cache/` (create that directory if necessary).
The build checks Oracle's published SHA-256, installs the SQL Server component
using the response template from the media, and creates a separate runtime image.
The installer is excluded from the runtime image and from Git.

From the repository root:

```powershell
./samples/scenarios/heterogeneous-lineage/run.ps1 -Gateway
# Also available through the scenario registry runner:
./samples/scripts/run-benchmark.ps1 -Scenario heterogeneous-lineage -Gateway
```

```bash
bash samples/scenarios/heterogeneous-lineage/run.sh --gateway
```

Alternatively, set `ORACLE_GATEWAY_IMAGE` in the **process environment** to an
already-installed local image. Pull it yourself first if it is in a registry.
That image must contain `/opt/oracle/product/19c/gateway/bin/dg4msql` and
`bin/lsnrctl`, their runtime dependencies, and a user with write access to that
Oracle home. The overlay supplies the listener configuration and entrypoint.
Using a prebuilt image bypasses the installer build; it does not bypass execution
tests. The packaged recipe uses Oracle Linux 7 and the Oracle 19.3 gateway media.

Missing media, failed installation, an unhealthy listener, failed remote reads,
permission mismatches, and catalog differences all fail the requested gateway
run. The default runner remains usable without the separately obtained software
and reports gateway execution as **not requested**.

## Routing

One listener on `HERMES_GATEWAY:1521` launches four gateway SIDs. It has no host
port binding. Oracle's existing local TNS entries are preserved when installing
the gateway aliases; reconfiguration replaces the benchmark's marked block.
The configuration also puts TNSNAMES before EZCONNECT in `sqlnet.ora`. Otherwise,
aliases such as ATLAS_SQL match Docker DNS names and Oracle attempts to connect
directly to SQL Server on Oracle's port, bypassing the gateway.

| Oracle TNS alias | Gateway SID | SQL destination |
|---|---|---|
| ATLAS_SQL | commerce | ATLAS_SQL / Commerce |
| ATLAS_SQL.Receivables | receivables | ATLAS_SQL / Receivables |
| MERIDIAN_SQL | distribution | MERIDIAN_SQL / Distribution |
| MERIDIAN_SQL.Intelligence | intelligence | MERIDIAN_SQL / Intelligence |

The qualified aliases retain the final SQL server identity for catalog
resolution. The gateway is transport infrastructure; it does not become a
fourth database server in the catalog.

PROCUREMENT owns REMOTE, SECONDARY and RECEIVABLES. COMPLIANCE owns REMOTE,
SECONDARY and INTELLIGENCE. REMOTE and SECONDARY deliberately swap SQL servers
between owners. Every link uses the `bridge_user` SQL login, an infrastructure
account outside the 48 workload users. It has SELECT on four designated ITEMS
tables and the remote-stock view, explicit SELECT denial on AUDIT_LOG, and no
write grants. Its SQL-to-SQL mapping uses the same restricted remote login.

Gateways use `HS_TRANSACTION_MODEL=READ_ONLY`. This benchmark validates reads
and permissions; it does not provision recovery accounts or test distributed
write transactions/two-phase commit.

## Tests

The independent catalog includes six additional objects: two database links,
three Oracle procedures, and `ORDER_ENTRY.V_REMOTE_STOCK`. The extra named path is:

```text
PROCUREMENT.P_STOCK
  -> ORDER_ENTRY.V_REMOTE_STOCK@REMOTE (ATLAS_SQL / Commerce)
  -> MERIDIAN_SQL.Distribution.INVENTORY.ITEMS
```

Ordinary tests validate the full catalog and all link-to-SID-to-database routes.
The regular Docker benchmark additionally seeds distinct values in each SQL
database and verifies bridge permissions and the SQL linked-server hop.

The gateway run adds six private-link reads (including all four databases), the
complete Oracle-to-SQL-to-SQL read, six Oracle procedure calls as EXEC workload
users, and denied procedure calls as READ users. It asserts remote SQL permission
errors separately from connectivity failures. It then extracts all three database
servers with the production CLI and compares the complete catalog to the same
independent contract. Nothing is merged into extraction from fixture metadata.

`benchmark.json` records whether gateway execution was requested and passed.
SQL Server-to-Oracle and circular execution remain unsupported in the Linux fleet;
their dependency paths remain part of the catalog assertions.

The complete local gateway run has been verified with the supplied Oracle 19.3
media: all four tests passed, including real reads through all six private links,
workload and bridge permission checks, the Oracle-to-SQL-to-SQL path, and the
174-object catalog with no unresolved references. The installed local image is
`syncsql-oracle-gateway:19.3`. This verifies the Docker workflow locally; a CI
runner must still be provisioned with that image before its gateway job can run.

## Publish the built image to private GHCR

The **Publish Oracle gateway to GHCR** workflow in
[cli-publish-oracle-gateway.yml](../../../../.github/workflows/cli-publish-oracle-gateway.yml)
publishes an image that is already installed on a self-hosted Linux x64 runner
labelled `oracle-gateway`. It runs manually from the repository's default branch.
It does not obtain the Oracle installer or build the gateway again.

1. Put the verified `syncsql-oracle-gateway:19.3` image on that runner. An image
   built on your workstation is not automatically visible to GitHub Actions.
   You can build it on the runner or transfer it using `docker save` / `docker load`:

   ```text
   docker save --output oracle-gateway-19.3.tar syncsql-oracle-gateway:19.3
   # Transfer the tar file to the runner, then execute there:
   docker load --input oracle-gateway-19.3.tar
   ```

2. Push the workflow and its helper scripts to the default branch. In GitHub,
   open **Actions → Publish Oracle gateway to GHCR → Run workflow**. The defaults
   are `source_image=syncsql-oracle-gateway:19.3` and `tag=19.3`.
3. The job pins the local image ID, runs the complete gateway benchmark, then
   publishes `ghcr.io/<repository-owner>/syncsql-oracle-gateway:19.3`. For this
   repository the owner is `cangelosilima`.
4. Copy the digest reference from the job summary into the repository Actions
   variable `ORACLE_GATEWAY_IMAGE`. This pins subsequent benchmark runs to that
   exact published image. If unset, the benchmark uses the GHCR `19.3` tag.

The workflow uses `GITHUB_TOKEN` with `packages: write`; no PAT secret is needed.
It refuses an existing public or internal package. On first publication, it pushes
a tiny empty image under `visibility-check-<run>-<attempt>` to create the package,
then requires the GitHub API to report `private` before uploading Oracle layers.
That small bootstrap tag remains in the private package. This avoids treating
the registry's default visibility as proof that the Oracle image is private.
It checks visibility again after publication and verifies the pulled image ID
matches the tested source. API/authentication failures stop publication.

The bootstrap image links the package to this repository. If an existing package
was created elsewhere, grant this repository access under the package's **Manage
Actions access** settings before publishing or pulling. The workflow cannot turn
an existing public package private; use a private package instead.

The **Heterogeneous database benchmark** workflow exposes the `gateway` dispatch
input. Its gateway job authenticates and pulls the GHCR image with `packages: read`
before running the tests. A local-image override remains supported. Publishing and
gateway benchmarking share a concurrency lock because both reset the same named
Docker fleet on the runner. PR jobs continue running the ordinary Docker benchmark
without requiring Oracle download access or package credentials.

Publishing guards run in ordinary CI with:

```text
node --test .github/scripts/gateway-package.test.cjs
```

Reference: [GHCR authentication, initial visibility and repository association](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry).

References: [Oracle gateway configuration](https://docs.oracle.com/en/database/oracle/oracle-database/19/otgis/config-sqlserver-gateway.html),
[Oracle silent installation](https://docs.oracle.com/en/database/oracle/oracle-database/19/otgis/using-response-files-for-noninteractive-installation.html).
