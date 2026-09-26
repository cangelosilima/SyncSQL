# Mutation testing

See the [CLI mutation investigation](mutation-investigation.md) for measured gaps,
regression tests, and limitations found in the first full Core/command runs.

Mutation testing checks whether tests detect small changes to production code.
Stryker.NET 5.0.0 runs against the seven CLI production projects and their matching
xUnit projects; StrykerJS 10.0.0 runs against the site application sources
using the existing Vitest configuration. Generated grammar, test code, and build
outputs are excluded. Database integration and sample benchmark suites are not
used: these runs require no database credentials or Docker services. Site packaging
scripts remain covered by ordinary Vitest tests; their subprocess execution and
fixtures outside the site directory need a separate mutation harness.

## Run locally

Use the .NET SDK selected by `global.json`, PowerShell 7, and Node.js 22 or 24 LTS.
CI uses Node.js 22. Node.js 25's built-in web storage can conflict with jsdom's
`localStorage` in the existing UI tests.
From the repository root:

```powershell
# Restore the pinned tool and mutate all seven CLI projects sequentially.
./scripts/Run-MutationTests.ps1

# Select one or more projects.
./scripts/Run-MutationTests.ps1 -Project Core,Catalog

# Fast targeted run; patterns are relative to the production project.
./scripts/Run-MutationTests.ps1 -Project Core -Mutate '**/NameFilter.cs'

# Enforce a chosen score for a run (0-100).
./scripts/Run-MutationTests.ps1 -Project Core -BreakAt 80
```

From `site/`:

```sh
npm ci
npm run test:mutation
npm run test:mutation -- --mutate src/lib/reachability.ts
# Run the required CI scope and its committed threshold.
npm run test:mutation -- stryker.ci.config.mjs
```

CLI reports are written under `TestResults/mutation/<project>/reports/`.
Use `-ArtifactsPath` to choose another output root. Site reports are written to
`site/reports/mutation/mutation.html` and `mutation.json`. Open the HTML report to
inspect individual mutations and the tests that killed them. A surviving mutation
means the selected tests did not detect it; add an assertion or a missing case after
checking whether the change can actually affect behavior. Mutations with no coverage
also reduce the score. Do not exclude survivors simply to raise the score.

## CI and score policy

The [Mutation testing workflow](../.github/workflows/mutation.yml) runs every Monday
and can be started manually with GitHub Actions. CLI projects run as separate jobs
(at most two concurrently); each Stryker run uses two workers. Jobs have a two-hour
limit and upload HTML/JSON reports with 14-day retention, including available output
after failures. Full runs can take substantially longer than ordinary unit tests.

The [Quality workflow](../.github/workflows/quality.yml) also runs mutation checks
on every pull request and merge-queue commit, as well as its other triggers. Both
mutation jobs are required by the aggregate **Quality gate**; failure, cancellation,
or a skipped job prevents the gate from passing. Configure **Quality gate** as a
required status check in branch protection to enforce it before merging.

| Required scope | Minimum score | Measured baseline |
| --- | ---: | ---: |
| Entire Core project | 90% | 91.60% |
| CLI `SyncCommand`, `CatalogCommand`, and `ExtractionProgressDisplay` | 55% | 56.32% |
| Site `src/lib/reachability.ts` | 100% | 100% |

These checks use full mutation runs of their specified scope, without change-only
filtering. Threshold failures and initial build/test failures fail CI. Reports are
uploaded even after failures, with 14-day retention. The CLI jobs have a 45-minute
limit and site reachability has a 20-minute limit. Results appear in the existing
quality-results PR comment. A targeted score is not a baseline for the whole project.

The broader weekly/manual workflow and local defaults retain a **0** threshold
while baselines are established for the remaining scopes. Surviving mutations in
those exploratory runs do not fail CI; build and initial test failures still do.

Configuration lives in `cli/stryker-config.json` and `site/stryker.config.json`.
The Quality workflow passes explicit `-BreakAt` overrides for .NET and uses
`site/stryker.ci.config.mjs` for the site, leaving exploratory defaults unchanged.
`-Mutate` accepts one or more patterns.
No results are uploaded to Stryker Dashboard.

Reference: [Stryker.NET configuration](https://stryker-mutator.io/docs/stryker-net/configuration/)
and [StrykerJS Vitest runner](https://stryker-mutator.io/docs/stryker-js/vitest-runner/).
