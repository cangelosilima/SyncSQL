# Publishing NuGet packages

The grammar and CLI have independent releases on nuget.org:

| Package | Workflow | Release tag |
| --- | --- | --- |
| `SyncSql.Grammar.PlSql` | `grammar-publish-nuget.yml` | `grammar-v2026.9.26` |
| `SyncSql.Cli` (.NET tool, command `syncsql`) | `cli-publish-nuget.yml` | `cli-v2026.9.26` |

The version comes from the tag, overriding the development version in
`cli/Directory.Build.props`. Releases use **CalVer `YYYY.M.D[.REV]`**: the release
date with no leading zeros on the month or day. For another release on the same
date, append an incrementing revision, starting at `.1` (for example,
`cli-v2026.9.26.1`). Revisions range from 1 to 65534 for .NET assembly compatibility.
Each package has its own revision sequence. Invalid calendar dates are rejected.
Prereleases such as `grammar-v2026.9.26-rc.1` are supported; build metadata is not.
Local builds default to the current UTC date followed by `-dev`.
Publishing the CLI does not require publishing
the grammar first: the tool bundles the grammar assembly through its existing
project reference. The standalone grammar targets .NET Standard 2.0 and declares
its ANTLR runtime dependency.

## One-time setup

1. Merge the workflows and package changes into the repository.
2. Add the Actions repository secret `NUGET_USER`, containing the NuGet profile
   username (not an email address) used to publish.
3. In nuget.org, configure two
   [trusted publishing policies](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
   for repository owner `cangelosilima`, repository `SyncSQL`. Set the workflow
   filenames to `grammar-publish-nuget.yml` and `cli-publish-nuget.yml`, respectively.
   Leave environment empty; these workflows do not use GitHub environments.
   Scope each policy to its corresponding package ID and permit publishing new
   packages as well as new versions if the package has not yet been published.
   The publishing account must own existing package IDs.

No long-lived NuGet API key is stored. The publish job obtains a temporary key
with GitHub OIDC after the package job succeeds, then uploads the tested artifact.

## Release

From the reviewed commit to release, push only the desired tag:

```bash
git tag grammar-v2026.9.26
git push origin grammar-v2026.9.26

# Independently, when ready to release the CLI:
git tag cli-v2026.9.26
git push origin cli-v2026.9.26
```

Each workflow runs tests, builds its package, and uploads a downloadable workflow
artifact before publishing. The CLI also installs the packed tool locally and
runs `syncsql --help`. Pull requests validate both packages without publishing.

For a manual dry run, run either workflow with **publish** unchecked. Branch runs
use the current UTC date with `-ci.RUN_NUMBER.RUN_ATTEMPT`; selecting a matching
release tag validates that exact version.
For a manual publication, select an existing matching release tag and check
**publish**. Branches and malformed versions cannot publish. Reruns skip versions
already on NuGet; use a new tag/version to release changed content.

## Consume

```bash
dotnet add package SyncSql.Grammar.PlSql
dotnet tool install --global SyncSql.Cli
```

The CLI requires the .NET 10 runtime. The grammar can be used independently by
.NET Standard 2.0 compatible applications.
