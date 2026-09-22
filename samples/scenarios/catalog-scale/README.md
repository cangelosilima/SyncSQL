# Catalog scale scenario

This database-free scenario creates a deterministic synthetic catalog for a large
fleet and compares three ways to persist and read it:

1. The current single JSON document.
2. A static layout with a small manifest plus per-database summary and detail
   JSON Lines files.
3. A SQLite file with indexed object identity and lineage columns, plus each
   complete object payload.

The default fleet matches the reported shape: 200,000 objects across 50 servers
and 80 databases. The generated files are disposable and are written under
`.cache/runs/catalog-scale/` by default; no large fixture is checked in. Each
object has representative DDL, columns, grants, appended sections, a short
history and metrics. Every object has a lineage edge, so edge lookup is part of
the comparison too.

Run with Python 3.10 or later:

```powershell
python samples/scenarios/catalog-scale/run.py
```

```bash
python3 samples/scenarios/catalog-scale/run.py
```

To smoke-test the scenario quickly, or change the fleet shape and output path:

```powershell
python samples/scenarios/catalog-scale/run.py --objects 1000 --servers 5 --databases 8
python samples/scenarios/catalog-scale/run.py --output .cache/runs/catalog-scale-small
```

The script prints generation time, bytes on disk (including gzip sizes for JSON),
and read timings for startup metadata, one database's summaries, one object's
details, exact identity lookup and a focused lineage lookup. It also verifies
that all three layouts contain the requested objects and edges. Timings describe
the local machine and filesystem; compare them on the same host and storage.

The JSON Lines static layout above is a comparison fixture, not the production catalog format.
Its JSON Lines partitions let the scenario write a large fleet with bounded
memory and model the requests a lazy-loading site would make. Browser integration,
global substring search behavior and incremental catalog updates still need
separate validation before adopting a format.

## Validate the production Pages format

Generate only the legacy source, then run the real site publisher (requires
`npm ci` in `site` first):

```sh
python samples/scenarios/catalog-scale/run.py --legacy-only
node samples/scenarios/catalog-scale/validate-pages.mjs
```

This verifies published summary counts and records actual compressed publication
bytes, initial catalog requests, decoded startup bytes and conversion time in
`.cache/runs/catalog-scale/pages-data/benchmark.json`. There are exactly 200,000
directed edges at the default size: one per object in a ring. This is not a
dense-graph stress test. Use `--input <catalog.json> --output <directory>` to
measure another catalog. These are file/publication measurements, not browser
latency or heap measurements. See the [production format documentation](../../../docs/partitioned-catalog.md)
for loading behavior and limitations.
