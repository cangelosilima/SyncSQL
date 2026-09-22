# Partitioned static catalog

The production site build converts the CLI's existing catalog JSON into a small
manifest and compressed, content-addressed partitions. GitHub and GitLab Pages
serve these files directly; no API, database server, range requests or custom
HTTP headers are required. The CLI output and checked-in demo remain compatible
with existing consumers. This changes publication, not extraction or the size
of the upstream catalog committed by the extraction pipeline.

## Build and preview

```sh
cd site
npm ci
npm run build
npm run preview
```

Replace `site/public/data/catalog.json` with your extracted catalog before
building. The published `dist/data/catalog.json` is a versioned manifest, with
payloads beside it under `dist/data/_catalog/`. Publish the entire `dist` folder.
The GitHub workflow already does this; the GitLab Pages job accepts either a
legacy catalog or a manifest accompanied by its sibling `_catalog` directory.
Relative URLs and hash routing preserve project Pages deployment paths.

To convert a catalog independently (paths below are relative to `site`):

```sh
npm run catalog:partition -- --input ../catalog.json --output ../catalog-static
```

Conversion streams nodes and edges from the input, retaining identity routing,
graph counts and compact summaries in memory. It does not parse the entire
original document at once. Temporary spool files are removed after conversion.
An already partitioned input is copied with its referenced payloads.

`npm run dev` serves the input directly, including legacy JSON. Use the production
build and preview commands above to exercise automatic partitioning.

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

The manifest is written after its payloads. Content hashes support HTTP caching
and reuse of unchanged partitions. Standalone conversion retains old payloads
by default; `--prune` removes unreferenced hash files in the output `_catalog`
directory. The production build prunes its fresh deployment artifact. A browser
using an old manifest after deployment may need to reload if its old files are
no longer hosted.

## Scale validation and limits

Run the [scale scenario](../samples/scenarios/catalog-scale/README.md) to measure
the actual publisher. One local synthetic run with 200,000 objects, 200,000
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
