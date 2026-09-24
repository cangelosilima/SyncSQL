# Partitioned static catalog

`syncsql catalog build` writes a small manifest and compressed, content-addressed
partitions by default, using the CLI's native .NET publisher. No Node.js runtime
is needed to generate a catalog. GitHub and GitLab Pages serve these files
directly; no API, database server, range requests or custom HTTP headers are required.
The site reads this format in development and production. Its production build
copies an existing manifest and payloads; conversion remains available for older
single-file catalogs and the checked-in demo.

## Consolidate engines and servers

```sh
syncsql catalog build
# Both ./MSSQL and ./ORACLE present: ./catalog/catalog.json + ./catalog/_catalog/
# One engine present: <engine>/catalog.json + <engine>/_catalog/

syncsql catalog build --output ./site/public/data/catalog.json
# Explicit inputs, including exports collected from separate servers:
syncsql catalog build --objects-root ./MSSQL ./ORACLE --output ./catalog/catalog.json
```

All selected roots are loaded before server canonicalization and lineage resolution.
This produces one inventory and graph spanning SQL Server, Oracle, and all included
servers. Existing site engine/server filters provide scoped views of that inventory.
Linked-server and database-link routes resolve across inputs when the link metadata
identifies a destination and the referenced object was extracted. Missing or ambiguous
destinations remain unresolved; consolidation cannot discover data that was not exported.
Use unique configured server names across engines; conflicting identities fail the build.

Each input automatically uses its own `metrics/` history when present;
`--metrics-root` overrides that for all inputs. Object paths are relative to the
inputs' common ancestor (or the selected root for one input). For git history,
`--path-prefix` identifies that ancestor inside `--repo-root`, so one mining pass
also captures commits and co-change pairs spanning engines. All inputs must be on
the same filesystem root; stage exports together when collecting from other machines.

Select `--objects-root ./MSSQL` for an engine-specific catalog. The CLI always
publishes the partitioned format. Generation still
holds the consolidated object graph in memory; partitioning reduces published payloads
and browser loading costs, not the catalog builder's memory requirement.

## Build and preview

Requires Node.js 22 or newer.

```sh
cd site
npm ci
npm run build
npm run preview
```

Generate directly into `site/public/data/catalog.json`, or copy both the manifest
and its sibling `_catalog/` directory there before building. The published
`dist/data/catalog.json` is a versioned manifest, with
payloads beside it under `dist/data/_catalog/`. Publish the entire `dist` folder.
The GitHub workflow already does this; the GitLab Pages job accepts either a
legacy catalog or a manifest accompanied by its sibling `_catalog` directory.
Relative URLs and hash routing preserve project Pages deployment paths.

To convert an older single-file catalog independently (paths below are relative to `site`):

```sh
npm run catalog:partition -- --input ../catalog.json --output ../catalog-static
```

This legacy conversion streams nodes and edges from the input, retaining identity routing,
graph counts and compact summaries in memory. It does not parse the entire
original document at once. Temporary spool files are removed after conversion.
An already partitioned input is copied with its referenced payloads.

`npm run dev` serves either CLI partitions or legacy JSON directly.

## What loads when

| Data | Loading behavior |
| --- | --- |
| Manifest and summaries | Startup: identities, column/grantee names, counts and compact recent metrics; all objects remain available to global inventory filters. |
| Details | Object selection: full columns, history and metrics, combined with SQL and grants; eight detail partitions cached. |
| SQL/search | DDL and broad substring searches scan candidate partitions in up to four workers; structured filters narrow candidates first. |
| Grants | Permission investigations and object details load matching permission partitions. |
| Edges/references | Focused lineage and server views load incident partitions; incoming and outgoing relationships are both indexed. Alerts load partitions containing orphan references. |

Object partitions stay within a database and target at most 1,024 objects or
2 MiB of original object JSON. A single oversized object is allowed. Summary
pages combine small object partitions to reduce startup requests. These object
limits do not cap high-degree edge payloads. Cross-partition edges are stored
at both endpoints and deduplicated by the client. Linked-server attribution,
column lineage, dynamic edges and unresolved/system references are preserved.

Full payloads use `.json.gz`; the browser decompresses them with the native
`DecompressionStream` API. Use a current browser. The loader also handles hosts
that decompress gzip responses themselves. Legacy uncompressed catalogs remain
readable. Detail and search failures display an error rather than silently
presenting an incomplete result, and stale navigation/search responses are ignored.

The CLI atomically replaces the manifest after its payloads. Content hashes support HTTP caching
and reuse of unchanged partitions. Standalone conversion retains old payloads
by default; `--prune` removes unreferenced hash files in the output `_catalog`
directory. The production build prunes its fresh deployment artifact. A browser
using an old manifest after deployment may need to reload if its old files are
no longer hosted.

## Scale validation and limits

Run the [scale scenario](../samples/scenarios/catalog-scale/README.md) to measure
the legacy Node.js converter. These measurements do not benchmark the native CLI
publisher. One local synthetic run with 200,000 objects, 200,000
edges, 50 servers and 80 databases produced:

| Measurement | Result |
| --- | ---: |
| Original catalog | 550,922,143 bytes |
| Published catalog files | 23,734,258 bytes |
| Initial catalog transfer, before optional manifest HTTP compression | 4,215,882 bytes |
| Initial catalog requests | 81 |
| Object partitions | 272 |
| Conversion time | 89.7 seconds |

These are synthetic file measurements, not a prediction for production data or
a browser latency benchmark. Repeated synthetic DDL compresses especially well.
Application assets and the optional AI model are excluded. All summaries still
decode at startup (about 130 MB of JSON in this scenario), so browser memory and
index-building costs remain proportional to object count. Global DDL search
still scans SQL partitions; it has no persistent full-text index. Loaded edge
partitions accumulate as the user explores. High-degree graphs and denser
lineage need their own stress tests; the scale fixture has one edge per object.

Integration tests round-trip the real heterogeneous demo through the publisher
and client, comparing complete details, permissions, multi-hop/cyclic/cross-server
lineage, substring searches, metrics alerts and aggregate counts with legacy
behavior. They also cover invalid manifests, missing partitions and cancellation.
