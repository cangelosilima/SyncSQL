# Windows package releases

`cli-publish-windows.yml` builds a self-contained Windows x64 CLI, runs
`syncsql --help`, packages it as a ZIP, and generates Chocolatey `syncsql`
and WinGet `cangelosilima.SyncSQL` metadata using that ZIP's SHA-256.
The .NET runtime is included. The PL/SQL grammar license and notice travel
with the executable. The repository currently has no application license;
the WinGet metadata says `Not specified`. Decide and document the application
license before the first community submission; do not claim an open-source
license merely because the repository is public.

## Setup and release

1. Create the GitHub Actions environment `windows-packages`. Add
   `CHOCO_API_KEY` for the Chocolatey account owning `syncsql`, and
   `WINGET_TOKEN` for a GitHub account able to fork and submit pull requests
   to `microsoft/winget-pkgs`. The default Actions token cannot do this.
   Confirm the proposed package identifiers are available before first release.
2. Merge relevant changes into protected `main`. The CLI NuGet workflow calls
   the reusable Windows workflow alongside its NuGet publish job. Both consume
   the same `YYYY.M.D.BUILD` version, `cli-v*` tag and tested commit.
3. Windows packaging uploads the ZIP, Chocolatey package and checksum to the
   CLI GitHub release, then submits to each registry independently. Configure
   environment reviewers if release submissions should require review.
4. Wait for Chocolatey moderation and the WinGet pull request to be accepted.
   Submission alone does not make the package searchable or installable from
   the public catalogs. Monitor the submission outputs for the review links.

Pull requests and manual runs of the CLI NuGet workflow validate both formats
without publishing. Only a push to `main` may publish; protect that branch with
required reviews and checks so its pushes come from approved merges. Tag pushes
cannot publish. Release assets are immutable: merge a new PR for changed
binaries. If a registry submission fails after assets are uploaded, rerun only
the failed registry job; do not rebuild the release.

## Install after registry acceptance

```powershell
choco install syncsql -y
choco upgrade syncsql -y
choco uninstall syncsql -y

winget install --id cangelosilima.SyncSQL --exact --source winget
winget upgrade --id cangelosilima.SyncSQL --exact --source winget
winget uninstall --id cangelosilima.SyncSQL --exact

syncsql --help
```

Use one installation method per machine to avoid competing `syncsql` commands
on PATH. Other operating systems can use the existing .NET global tool.

## Local validation

```powershell
node --test .github/scripts/windows-package.test.cjs
dotnet publish cli/src/SyncSql.Cli/SyncSql.Cli.csproj -c Release -r win-x64 --self-contained true -p:WindowsStandalone=true -p:Version=2026.10.3 -o artifacts/windows/publish
Compress-Archive artifacts/windows/publish/* artifacts/windows/syncsql-2026.10.3-win-x64.zip
node .github/scripts/windows-package.cjs 2026.10.3 artifacts/windows/syncsql-2026.10.3-win-x64.zip artifacts/windows/packages
choco pack artifacts/windows/packages/chocolatey/syncsql.nuspec --outputdirectory artifacts/windows
winget validate artifacts/windows/packages/winget/manifests/c/cangelosilima/SyncSQL/2026.10.3
```

Before the initial submission, test installation, upgrade and removal in a
disposable Windows x64 machine using the public release URL. For a local
Chocolatey package, install with `choco install syncsql --source <package-directory>`.
For WinGet, enable local manifests and use `winget install --manifest <version-directory>`.
These installations still download the published release ZIP.

The manifests follow [WinGet's manifest format](https://learn.microsoft.com/windows/package-manager/package/manifest)
and submissions use [Microsoft's WingetCreate](https://github.com/microsoft/winget-create).
