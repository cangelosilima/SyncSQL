# Catalog example and GitHub Pages

The default catalog is a real extracted snapshot of the **heterogeneous-lineage**
benchmark: Helios Oracle, Atlas SQL Server and Meridian SQL Server, with 174
objects, 48 workload users and six attributed lineage paths. The overview provides
object, user-access and path entry points. Hash routing and relative assets support
GitHub project Pages, including reloads and shared object links.

The sample uses synthetic data. Oracle gateway reads and the Oracle → SQL Server
→ SQL Server read were executed successfully. SQL Server → Oracle links are
explicitly labeled metadata placeholders on Linux. The circular paths demonstrate
catalog lineage, not successful recursive SQL execution. Grants show recorded
permissions, not effective authorization through remote login mappings.

## Run locally

```sh
cd site
npm ci
npm run example:check
npm run dev
```

## Refresh from the benchmark

Run `samples/scenarios/heterogeneous-lineage/run.ps1 -Gateway` (Windows) or
`run.sh --gateway` (Linux), then pass its successful run directory:

```sh
cd site
npm run example:refresh -- ../samples/scenarios/heterogeneous-lineage/.cache/runs/<run-id>
npm test
npm run build
```

The importer requires a passing gateway benchmark report and compares extracted
identities, columns, grants, complete edges and attributed remote references
against the independent expected catalog. It refuses unmasked link passwords.
Only the catalog and safe example metadata are copied; connection files, database
contents, raw logs and Oracle installation media are not part of the site.

## Publish

[Site - deploy catalog demo](../.github/workflows/site-deploy.yml) tests and builds the
committed snapshot on GitHub-hosted Linux, including the vendored browser AI model.
Pull requests build for validation; main-branch changes deploy to GitHub Pages.
It can also be run manually on `main`. Set repository **Settings → Pages → Source**
to **GitHub Actions** once. No database, gateway image or database credentials are
needed on the Pages runner. Gateway image publication is a separate workflow.

The expected project URL is [the SyncSQL catalog demo](https://cangelosilima.github.io/SyncSQL/).
The existing GitLab pipeline can continue replacing `public/data/catalog.json`
with its own catalog; without example metadata, no benchmark guide is displayed.
