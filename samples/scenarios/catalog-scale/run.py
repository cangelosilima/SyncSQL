#!/usr/bin/env python3
"""Generate and measure catalog storage layouts at fleet scale."""

from __future__ import annotations

import argparse
import gzip
import json
import sqlite3
import time
from pathlib import Path
from typing import Any


def object_record(index: int, servers: int, databases: int, object_count: int) -> dict[str, Any]:
    database_index = index % databases
    server_index = database_index % servers
    database_on_server = database_index // servers
    server = f"SERVER_{server_index + 1:02d}"
    database = f"DATABASE_{database_index + 1:03d}"
    schema = f"SCHEMA_{index % 32 + 1:02d}"
    kind = index % 5
    object_type = ("Tables", "Views", "StoredProcedures", "Functions", "Tables")[kind]
    name = f"Object_{index + 1:06d}"
    object_id = f"{server}/{database}/{schema}/{object_type}/{name}"
    next_index = (index + 1) % object_count
    target_database_index = next_index % databases
    target_server = f"SERVER_{target_database_index % servers + 1:02d}"
    target_database = f"DATABASE_{target_database_index + 1:03d}"
    target_type = ("Tables", "Views", "StoredProcedures", "Functions", "Tables")[next_index % 5]
    target_id = f"{target_server}/{target_database}/SCHEMA_{next_index % 32 + 1:02d}/{target_type}/Object_{next_index + 1:06d}"
    columns = [
        {"name": f"Column_{column + 1:02d}", "description": None, "dataType": "nvarchar(128)"}
        for column in range(8 if object_type == "Tables" else 0)
    ]
    ddl = (
        f"CREATE {object_type[:-1].upper()} [{schema}].[{name}] AS\n"
        f"SELECT source.Id, source.Column_01, source.Column_02\n"
        f"FROM [{target_database}].[SCHEMA_{next_index % 32 + 1:02d}].[Object_{next_index + 1:06d}] AS source;\n"
        f"-- Synthetic scale fixture; server shard {server_index + 1}, database shard {database_on_server + 1}.\n"
    )
    return {
        "id": object_id,
        "server": server,
        "database": database,
        "schema": schema,
        "type": object_type,
        "name": name,
        "qualifiedName": f"{schema}.{name}",
        "path": f"{server}/{database}/{schema}/{object_type}/{name}.sql",
        "ddl": ddl,
        "description": f"Synthetic {object_type} object {index + 1} for catalog scale measurements.",
        "columns": columns,
        "grants": [
            {"permission": permission, "state": "GRANT", "grantee": f"ROLE_{role}", "granteeType": "DATABASE_ROLE", "column": None}
            for role, permission in (("READ", "SELECT"), ("WRITE", "UPDATE"), ("EXEC", "EXECUTE"))
        ],
        "sections": [{"title": "Indexes", "content": f"CREATE INDEX IX_{name} ON [{schema}].[{name}] (Column_01);"}],
        "sizeBytes": len(ddl.encode("utf-8")),
        "changeCount": 12,
        "lastChangedAt": "2026-09-20T12:00:00+00:00",
        "history": [
            {"sha": f"{index + version:040x}", "date": f"2026-09-{20 - version:02d}T12:00:00+00:00", "message": f"Synthetic revision {version}", "ddl": ddl}
            for version in range(3)
        ],
        "metrics": [{"capturedAt": "2026-09-20T12:00:00+00:00", "rowCount": index * 17, "reservedKB": index % 50000, "dataKB": index % 40000, "indexKB": index % 10000, "indexes": [], "statistics": []}],
        "engine": "mssql",
        "_target": target_id,
    }


def json_line(value: Any) -> str:
    return json.dumps(value, separators=(",", ":"), ensure_ascii=False)


def timed(action):
    started = time.perf_counter()
    value = action()
    return value, time.perf_counter() - started


def write_gzip(source: Path, destination: Path) -> int:
    with source.open("rb") as source_file, gzip.open(destination, "wb", compresslevel=6) as compressed:
        while block := source_file.read(1024 * 1024):
            compressed.write(block)
    size = destination.stat().st_size
    destination.unlink()
    return size


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--objects", type=int, default=200_000)
    parser.add_argument("--servers", type=int, default=50)
    parser.add_argument("--databases", type=int, default=80)
    parser.add_argument("--output", type=Path, default=Path(".cache/runs/catalog-scale"))
    parser.add_argument("--legacy-only", action="store_true", help="Only generate the streaming JSON input for the production Pages partitioner")
    args = parser.parse_args()
    if args.objects < 1 or args.servers < 1 or args.databases < 1 or args.servers > args.databases:
        parser.error("objects and servers must be positive, and databases must be at least servers")

    output = args.output.resolve()
    legacy_dir = output / "single-json"
    static_dir = output / "static"
    legacy_dir.mkdir(parents=True, exist_ok=True)
    if args.legacy_only:
        legacy_path = legacy_dir / "catalog.json"
        type_counts: dict[str, int] = {}
        with legacy_path.open("w", encoding="utf-8", newline="\n") as legacy:
            legacy.write('{"generatedAt":"2026-09-21T00:00:00+00:00","servers":')
            legacy.write(json_line([f"SERVER_{index + 1:02d}" for index in range(args.servers)]))
            legacy.write(',"nodes":[')
            for index in range(args.objects):
                node = object_record(index, args.servers, args.databases, args.objects)
                node.pop("_target")
                type_counts[node["type"]] = type_counts.get(node["type"], 0) + 1
                legacy.write(("," if index else "") + json_line(node))
            legacy.write('],"edges":[')
            for index in range(args.objects):
                node = object_record(index, args.servers, args.databases, args.objects)
                legacy.write(("," if index else "") + json_line({"from": node["id"], "to": node["_target"], "columns": [], "dynamic": False}))
            legacy.write('],"recentChanges":[],"coChangePairs":[],"typeCounts":' + json_line(type_counts) + '}')
        print(f"Generated {args.objects:,} objects and edges: {legacy_path} ({legacy_path.stat().st_size / 1_000_000:.1f} MB)")
        return
    (static_dir / "summaries").mkdir(parents=True, exist_ok=True)
    (static_dir / "details").mkdir(parents=True, exist_ok=True)
    sqlite_path = output / "catalog.sqlite"
    if sqlite_path.exists():
        sqlite_path.unlink()

    partitions: list[dict[str, Any]] = []
    for database_index in range(args.databases):
        server = f"SERVER_{database_index % args.servers + 1:02d}"
        database = f"DATABASE_{database_index + 1:03d}"
        relative = f"{server}/{database}.jsonl"
        partitions.append({"server": server, "database": database, "summaries": f"summaries/{relative}", "details": f"details/{relative}", "edges": f"edges/{relative}"})
        for kind in ("summaries", "details", "edges"):
            (static_dir / kind / server).mkdir(parents=True, exist_ok=True)

    manifest = {"version": 1, "generatedAt": "2026-09-21T00:00:00+00:00", "servers": [f"SERVER_{index + 1:02d}" for index in range(args.servers)], "databaseCount": args.databases, "objectCount": args.objects, "partitions": partitions}
    (static_dir / "manifest.json").write_text(json_line(manifest), encoding="utf-8")

    summary_paths = [static_dir / part["summaries"] for part in partitions]
    detail_paths = [static_dir / part["details"] for part in partitions]
    edge_paths = [static_dir / part["edges"] for part in partitions]
    summary_files = [path.open("w", encoding="utf-8", newline="\n") for path in summary_paths]
    detail_files = [path.open("w", encoding="utf-8", newline="\n") for path in detail_paths]
    edge_files = [path.open("w", encoding="utf-8", newline="\n") for path in edge_paths]
    legacy_path = legacy_dir / "catalog.json"
    db = sqlite3.connect(sqlite_path)
    db.execute("PRAGMA journal_mode=OFF")
    db.execute("PRAGMA synchronous=OFF")
    db.execute("CREATE TABLE objects (id TEXT PRIMARY KEY, server TEXT NOT NULL, database_name TEXT NOT NULL, schema_name TEXT, type TEXT NOT NULL, name TEXT NOT NULL, detail_json TEXT NOT NULL)")
    db.execute("CREATE TABLE edges (from_id TEXT NOT NULL, to_id TEXT NOT NULL)")
    insert_objects: list[tuple[str, str, str, str, str, str, str]] = []
    insert_edges: list[tuple[str, str]] = []

    started = time.perf_counter()
    try:
        with legacy_path.open("w", encoding="utf-8", newline="\n") as legacy:
            legacy.write('{"generatedAt":"2026-09-21T00:00:00+00:00","servers":[')
            legacy.write(",".join(json.dumps(f"SERVER_{index + 1:02d}") for index in range(args.servers)))
            legacy.write('],"nodes":[\n')
            for index in range(args.objects):
                node = object_record(index, args.servers, args.databases, args.objects)
                partition = index % args.databases
                target = node.pop("_target")
                legacy.write(("," if index else "") + json_line(node))
                summary = {key: node[key] for key in ("id", "server", "database", "schema", "type", "name", "qualifiedName", "path", "description", "sizeBytes", "changeCount", "lastChangedAt")}
                summary_files[partition].write(json_line(summary) + "\n")
                detail_files[partition].write(json_line(node) + "\n")
                edge = {"from": node["id"], "to": target, "columns": [], "dynamic": False}
                edge_json = json_line(edge) + "\n"
                edge_files[partition].write(edge_json)
                target_partition = ((index + 1) % args.objects) % args.databases
                if target_partition != partition:
                    # Duplicate cross-partition edges so either endpoint's partition
                    # can answer its focused incoming/outgoing lineage request.
                    edge_files[target_partition].write(edge_json)
                insert_objects.append((node["id"], node["server"], node["database"], node["schema"], node["type"], node["name"], json_line(node)))
                insert_edges.append((node["id"], target))
                if len(insert_objects) >= 1000:
                    db.executemany("INSERT INTO objects VALUES (?, ?, ?, ?, ?, ?, ?)", insert_objects)
                    db.executemany("INSERT INTO edges VALUES (?, ?)", insert_edges)
                    insert_objects.clear()
                    insert_edges.clear()
            if insert_objects:
                db.executemany("INSERT INTO objects VALUES (?, ?, ?, ?, ?, ?, ?)", insert_objects)
                db.executemany("INSERT INTO edges VALUES (?, ?)", insert_edges)
            legacy.write('],"edges":[\n')
            for index in range(args.objects):
                source = object_record(index, args.servers, args.databases, args.objects)
                edge = {"from": source["id"], "to": source["_target"], "columns": [], "dynamic": False}
                legacy.write(("," if index else "") + json_line(edge))
            legacy.write("]}")
        db.commit()
    finally:
        for file in summary_files + detail_files + edge_files:
            file.close()
    generation_seconds = time.perf_counter() - started
    db.execute("CREATE INDEX ix_objects_scope ON objects(server, database_name, type, name)")
    db.execute("CREATE INDEX ix_edges_from ON edges(from_id)")
    db.execute("CREATE INDEX ix_edges_to ON edges(to_id)")
    db.commit()

    legacy_size = legacy_path.stat().st_size
    static_bytes = sum(path.stat().st_size for path in static_dir.rglob("*.*"))
    sqlite_size = sqlite_path.stat().st_size
    legacy_gzip, legacy_compression_seconds = timed(lambda: write_gzip(legacy_path, output / "catalog.json.gz"))
    static_gzip = 0
    for path in static_dir.rglob("*.json*"):
        static_gzip += write_gzip(path, output / "partition.tmp.gz")

    legacy_document, startup_json_seconds = timed(lambda: json.loads(legacy_path.read_text(encoding="utf-8")))
    assert len(legacy_document["nodes"]) == args.objects
    assert len(legacy_document["edges"]) == args.objects
    del legacy_document
    manifest_result, startup_static_seconds = timed(lambda: json.loads((static_dir / "manifest.json").read_text(encoding="utf-8")))
    sample_partition = args.databases // 2
    _, one_database_seconds = timed(lambda: [json.loads(line) for line in summary_paths[sample_partition].read_text(encoding="utf-8").splitlines()])
    target_index = min(args.objects - 1, args.objects // 2)
    target_node = object_record(target_index, args.servers, args.databases, args.objects)
    detail_path = detail_paths[target_index % args.databases]
    def read_matching_detail():
        with detail_path.open(encoding="utf-8") as details:
            for line in details:
                item = json.loads(line)
                if item["id"] == target_node["id"]:
                    return item
        return None

    static_object, one_object_seconds = timed(read_matching_detail)
    assert static_object is not None
    edge_path = edge_paths[target_index % args.databases]
    def read_matching_edges():
        with edge_path.open(encoding="utf-8") as edges:
            return [edge for line in edges if (edge := json.loads(line))["from"] == target_node["id"] or edge["to"] == target_node["id"]]

    static_edges, static_lineage_seconds = timed(read_matching_edges)
    identity_params = (target_node["server"], target_node["database"], target_node["type"], target_node["name"])
    sqlite_object, object_lookup_seconds = timed(lambda: db.execute("SELECT detail_json FROM objects WHERE server=? AND database_name=? AND type=? AND name=?", identity_params).fetchone())
    sqlite_edges, lineage_lookup_seconds = timed(lambda: db.execute("SELECT from_id, to_id FROM edges WHERE from_id=? OR to_id=?", (target_node["id"], target_node["id"])).fetchall())

    counts = db.execute("SELECT (SELECT count(*) FROM objects), (SELECT count(*) FROM edges)").fetchone()
    assert counts == (args.objects, args.objects), f"SQLite row counts differ: {counts}"
    assert sqlite_object is not None and json.loads(sqlite_object[0])["id"] == target_node["id"]
    assert len(static_edges) >= 1
    assert manifest_result["objectCount"] == args.objects
    db.close()

    print(f"Catalog scale scenario: {args.objects:,} objects, {args.servers} servers, {args.databases} databases")
    print(f"Output: {output}")
    print(f"Generation and SQLite load: {generation_seconds:.2f}s")
    print("\nOn-disk size:")
    print(f"  Single JSON:     {legacy_size / 1_000_000:.1f} MB ({legacy_gzip / 1_000_000:.1f} MB gzip; compression {legacy_compression_seconds:.2f}s)")
    print(f"  Static shards:   {static_bytes / 1_000_000:.1f} MB ({static_gzip / 1_000_000:.1f} MB gzip)")
    print(f"  SQLite:          {sqlite_size / 1_000_000:.1f} MB")
    print("\nRead timings (single cold/warm local run; use repeated runs for comparison):")
    print(f"  Parse full JSON at startup:       {startup_json_seconds:.3f}s")
    print(f"  Read static manifest:             {startup_static_seconds:.6f}s")
    print(f"  Read one database's summaries:   {one_database_seconds:.3f}s")
    print(f"  Find one object in its detail shard: {one_object_seconds:.3f}s")
    print(f"  SQLite exact object lookup:       {object_lookup_seconds:.6f}s")
    print(f"  Static focused lineage lookup:   {static_lineage_seconds:.3f}s ({len(static_edges)} adjacent edge(s))")
    print(f"  SQLite focused lineage lookup:    {lineage_lookup_seconds:.6f}s ({len(sqlite_edges)} adjacent edge(s))")
    print("\nChecks: object/edge totals and indexed object payload lookup passed.")


if __name__ == "__main__":
    main()
