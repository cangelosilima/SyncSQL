const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const crypto = require('node:crypto');
const { generate } = require('./windows-package.cjs');

test('both package managers consume the same release URL and archive checksum', (t) => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'syncsql-packaging-'));
  t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  const archive = path.join(dir, 'syncsql-2026.10.3.1-win-x64.zip');
  fs.writeFileSync(archive, 'fixture archive');
  const output = path.join(dir, 'output');
  generate('2026.10.3.1', archive, output);
  const hash = crypto.createHash('sha256').update('fixture archive').digest('hex').toUpperCase();
  const install = fs.readFileSync(path.join(output, 'chocolatey/tools/chocolateyinstall.ps1'), 'utf8');
  const manifest = fs.readFileSync(path.join(output, 'winget/manifests/c/cangelosilima/SyncSQL/2026.10.3.1/cangelosilima.SyncSQL.installer.yaml'), 'utf8');
  for (const content of [install, manifest]) {
    assert.ok(content.includes(hash));
    assert.ok(content.includes('https://github.com/cangelosilima/SyncSQL/releases/download/cli-v2026.10.3.1/syncsql-2026.10.3.1-win-x64.zip'));
  }
  assert.match(manifest, /RelativeFilePath: syncsql.exe/);
  assert.match(manifest, /PortableCommandAlias: syncsql/);
  assert.match(install, /ChecksumType64 'sha256'/);
});

test('rejects prereleases, invalid dates, unsafe versions and mismatched assets', () => {
  for (const version of ['2026.2.29', '2026.04.3', '2026.10.3-rc.1', '2026.10.3.65535', '../1', '2026.13.1']) {
    assert.throws(() => generate(version, 'missing.zip', 'unused'), /stable CalVer/);
  }
  assert.throws(() => generate('2026.10.3', 'wrong.zip', 'unused'), /Expected archive/);
});
