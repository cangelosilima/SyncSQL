# Publishing NuGet packages

The grammar and CLI publish independently when relevant changes are merged into
`main`. Each workflow tests and packages its component, publishes it to nuget.org,
and creates a GitHub release with generated notes and the `.nupkg` attached.

| Package | Workflow | Example generated release tag |
| --- | --- | --- |
| `SyncSql.Grammar.PlSql` | `grammar-publish-nuget.yml` | `grammar-v2026.9.26.42` |
| `SyncSql.Cli` (.NET tool, command `syncsql`) | `cli-publish-nuget.yml` | `cli-v2026.9.26.57` |

## CalVer

Versions use **`YYYY.M.D.BUILD`**, without leading zeros. The date is the tested
main commit's UTC committer date, and BUILD is the workflow's run number. Each
workflow has its own sequence; validation runs can leave gaps. BUILD must be
between 1 and 65534 for .NET assembly compatibility.

The date and run number stay the same when a failed run is rerun, even on another
day. The generated version overrides `cli/Directory.Build.props`; local builds
default to the current UTC date followed by `-dev`.

Publishing the CLI does not require publishing the grammar first: the tool
bundles the grammar assembly through its project reference. The standalone
grammar targets .NET Standard 2.0 and declares its ANTLR runtime dependency.

## One-time setup

Before merging the workflows:

1. Set the Actions repository secret `NUGET_USER` to the NuGet profile username
   (currently `this.programmer`, not an email address).
2. In nuget.org, configure two
   [trusted publishing policies](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
   for owner `cangelosilima`, repository `SyncSQL`. Use workflow filenames
   `grammar-publish-nuget.yml` and `cli-publish-nuget.yml`.
   Leave environment empty; the workflows do not use GitHub environments.
   Scope each policy to its package ID and permit new packages as well as new
   versions for the first publication. The account must own existing package IDs.
3. Allow these workflows to create release tags and GitHub releases. The publish
   job requests `contents: write` and `id-token: write`.

No long-lived NuGet API key is stored. The publish job obtains a temporary key
with GitHub OIDC after the package job succeeds.

## Release and recovery

Merge the reviewed changes into `main`; do not create tags manually. Only
`push` events on `refs/heads/main` may publish. Tag pushes, pull requests,
other branches, and manual workflow runs cannot publish or create releases.
Protect `main` with the repository's required reviews and checks to ensure
its push events come from approved merges.

The CLI workflow watches CLI, grammar, SDK, and publishing-automation changes.
The grammar workflow watches grammar sources, its project, relevant Oracle
lineage/Core sources and tests, shared build settings, SDK, and its publishing
automation. A CLI-only change does not publish a new grammar package.

For each selected package, the workflow:

1. Runs tests, builds the package, and uploads a workflow artifact.
2. For the CLI, installs the packed tool and runs `syncsql --help`.
3. Reserves the generated tag at the exact tested main commit.
4. Publishes that package to NuGet.
5. Creates a draft GitHub release with generated notes, attaches the package,
   then publishes the release.

Rerun a failed workflow to recover. It reuses its version and tag, skips an
already-published NuGet version, and finishes an incomplete GitHub release.
An existing tag pointing to another commit is rejected and never moved.
A failure before NuGet publication can leave a reserved tag; a failure during
asset upload can leave a draft release. Neither triggers another publishing run.

Pull requests and manual runs only validate, with versions suffixed
`-ci.RUN_ATTEMPT`. Use **Run workflow** for a dry run; there is no manual publish
option. GitHub releases for the two packages do not replace each other as the
repository's latest release.

The CLI workflow also calls the reusable Windows packaging workflow alongside
its NuGet publish job. Chocolatey and WinGet use the CLI version, release tag
and tested commit. See [Windows package releases](windows-packaging.md) for
registry credentials, validation and installation commands.

After CLI publication, fresh Linux, Windows and macOS runners install the exact
version directly from nuget.org. Each runner retries `dotnet tool install` up to
20 times with 30 seconds between attempts, then verifies the command, version,
help output and uninstall. Package download availability can precede feed lookup
availability, so the actual installation determines readiness.

## Consume

```bash
dotnet add package SyncSql.Grammar.PlSql
dotnet tool install --global SyncSql.Cli
```

The CLI requires the .NET 10 runtime. The grammar can be used independently by
.NET Standard 2.0 compatible applications.
