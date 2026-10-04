const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');

function generate(version, archive, output) {
  const match = /^(\d{4})\.([1-9]|1[0-2])\.([1-9]|[12]\d|3[01])(?:\.([1-9]\d{0,4}))?$/.exec(version);
  if (!match || Number(match[4] || 0) > 65534 || Number(match[3]) > new Date(Date.UTC(Number(match[1]), Number(match[2]), 0)).getUTCDate()) {
    throw new Error('Windows packages require a stable CalVer YYYY.M.D[.REV].');
  }
  const filename = `syncsql-${version}-win-x64.zip`;
  if (path.basename(archive) !== filename) throw new Error(`Expected archive ${filename}`);
  const hash = crypto.createHash('sha256').update(fs.readFileSync(archive)).digest('hex').toUpperCase();
  const home = 'https://github.com/cangelosilima/SyncSQL';
  const url = `${home}/releases/download/cli-v${version}/${filename}`;
  const write = (name, content) => {
    const target = path.join(output, name);
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.writeFileSync(target, content);
  };
  write('chocolatey/syncsql.nuspec', `<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2015/06/nuspec.xsd">
  <metadata>
    <id>syncsql</id><version>${version}</version>
    <title>SQLineage CLI (syncsql)</title><authors>cangelosilima</authors>
    <projectUrl>${home}</projectUrl><packageSourceUrl>${home}</packageSourceUrl>
    <requireLicenseAcceptance>false</requireLicenseAcceptance>
    <summary>SQL Server and Oracle extraction, lineage and catalog CLI.</summary>
    <description>Self-contained Windows x64 CLI for database extraction, lineage analysis and catalog building. No separate .NET runtime or native Oracle client is required.</description>
    <tags>sql database lineage oracle cli</tags>
  </metadata>
  <files><file src="tools\\**" target="tools" /></files>
</package>
`);
  write('chocolatey/tools/chocolateyinstall.ps1', `$ErrorActionPreference = 'Stop'
if (-not [Environment]::Is64BitOperatingSystem -or $env:PROCESSOR_ARCHITECTURE -eq 'ARM64' -or $env:PROCESSOR_ARCHITEW6432 -eq 'ARM64') { throw 'syncsql requires Windows x64.' }
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
Install-ChocolateyZipPackage -PackageName 'syncsql' -Url64bit '${url}' -UnzipLocation $toolsDir -Checksum64 '${hash}' -ChecksumType64 'sha256'
# Only expose the CLI, not other executables shipped by the runtime.
Get-ChildItem $toolsDir -Filter *.exe -Recurse | Where-Object Name -ne 'syncsql.exe' | ForEach-Object { New-Item -ItemType File -Path ($_.FullName + '.ignore') -Force | Out-Null }
`);
  const prefix = `PackageIdentifier: cangelosilima.SyncSQL\nPackageVersion: ${version}\n`;
  const dir = `winget/manifests/c/cangelosilima/SyncSQL/${version}`;
  write(`${dir}/cangelosilima.SyncSQL.yaml`, `${prefix}DefaultLocale: en-US\nManifestType: version\nManifestVersion: 1.6.0\n`);
  write(`${dir}/cangelosilima.SyncSQL.locale.en-US.yaml`, `${prefix}PackageLocale: en-US
Publisher: cangelosilima
PackageName: SQLineage CLI
PackageUrl: ${home}
License: Not specified
ShortDescription: SQL Server and Oracle extraction, lineage and catalog CLI.
Moniker: syncsql
ManifestType: defaultLocale
ManifestVersion: 1.6.0
`);
  write(`${dir}/cangelosilima.SyncSQL.installer.yaml`, `${prefix}InstallerType: zip
NestedInstallerType: portable
NestedInstallerFiles:
  - RelativeFilePath: syncsql.exe
    PortableCommandAlias: syncsql
Installers:
  - Architecture: x64
    InstallerUrl: ${url}
    InstallerSha256: ${hash}
ManifestType: installer
ManifestVersion: 1.6.0
`);
  write(`${filename}.sha256`, `${hash}  ${filename}\n`);
}

module.exports = { generate };
if (require.main === module) {
  if (process.argv.length !== 5) throw new Error('Usage: node windows-package.cjs VERSION ARCHIVE OUTPUT');
  generate(...process.argv.slice(2));
}
