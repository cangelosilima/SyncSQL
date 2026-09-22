import { readFile, stat, writeFile } from "node:fs/promises";
import path from "node:path";
import { gzipSync, gunzipSync } from "node:zlib";
import { partitionCatalog } from "../../../site/scripts/partition-catalog.mjs";

const args = process.argv.slice(2);
const option = (name, fallback) =>
  args.includes(name) ? args[args.indexOf(name) + 1] : fallback;
const input = path.resolve(
  option("--input", ".cache/runs/catalog-scale/single-json/catalog.json"),
);
const output = path.resolve(
  option("--output", ".cache/runs/catalog-scale/pages-data"),
);
const start = performance.now();
const manifest = await partitionCatalog(input, output, { prune: true });
const buildSeconds = (performance.now() - start) / 1000;
const startupFiles = [
  "catalog.json",
  ...manifest.summaries.map((part) => part.file),
];
const files = [
  ...new Set([
    ...startupFiles,
    ...manifest.partitions.flatMap((part) => [
      part.details,
      part.edges,
      part.search,
      part.grants,
    ]),
  ]),
];
let initialBytes = 0;
let initialGzipBytes = 0;
let initialDecodedBytes = 0;
let initialObjects = 0;
for (const file of startupFiles) {
  const data = await readFile(path.join(output, file));
  initialBytes += data.byteLength;
  initialGzipBytes += file.endsWith(".gz")
    ? data.byteLength
    : gzipSync(data).byteLength;
  const decoded = file.endsWith(".gz") ? gunzipSync(data) : data;
  initialDecodedBytes += decoded.byteLength;
  if (file !== "catalog.json")
    for (const group of JSON.parse(decoded))
      initialObjects += group.nodes.length;
}
if (initialObjects !== manifest.nodeCount)
  throw new Error("Startup inventory lost objects");
let publishedBytes = 0;
for (const file of files)
  publishedBytes += (await stat(path.join(output, file))).size;
const report = {
  objects: manifest.nodeCount,
  edges: manifest.edgeCount,
  partitions: manifest.partitions.length,
  inputBytes: (await stat(input)).size,
  publishedBytes,
  initialBytes,
  initialGzipBytes,
  initialDecodedBytes,
  initialRequests: startupFiles.length,
  publishedFiles: files.length,
  buildSeconds,
  note: "Local synthetic publication measurements. Partitions are published compressed. Startup loads all summaries; no detail, search or edge files. initialGzipBytes additionally estimates host gzip compression of the small manifest.",
};
await writeFile(
  path.join(output, "benchmark.json"),
  JSON.stringify(report, null, 2),
);
console.log(JSON.stringify(report, null, 2));
