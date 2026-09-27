const { test, after } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { reserveTag, publishRelease } = require('./nuget-release.cjs');

const artifacts = fs.mkdtempSync(path.join(os.tmpdir(), 'syncsql-nuget-release-'));
after(() => fs.rmSync(artifacts, { recursive: true }));
fs.writeFileSync(path.join(artifacts, 'SyncSql.Cli.2026.9.26.42.nupkg'), 'package bytes');

function fixture() {
  const calls = [];
  const state = { tag: null, release: null, assets: [] };
  const missing = () => { throw Object.assign(new Error('Not found'), { status: 404 }); };
  const github = {
    rest: {
      git: {
        getRef: async () => state.tag ? { data: state.tag } : missing(),
        createRef: async args => {
          calls.push(['tag', args]);
          state.tag = { object: { type: 'commit', sha: args.sha } };
        }
      },
      repos: {
        getReleaseByTag: async () => state.release ? { data: state.release } : missing(),
        createRelease: async args => {
          calls.push(['draft', args]);
          state.release = { id: 7, draft: true };
          return { data: state.release };
        },
        listReleaseAssets: async () => state.assets,
        deleteReleaseAsset: async args => { calls.push(['delete', args]); state.assets = []; },
        uploadReleaseAsset: async args => {
          calls.push(['upload', args]);
          state.assets.push({ id: 8, name: args.name, state: 'uploaded' });
        },
        updateRelease: async args => {
          calls.push(['publish', args]);
          state.release.draft = args.draft;
        }
      }
    },
    paginate: async method => method()
  };
  return {
    calls, state, github, artifacts, tag: 'cli-v2026.9.26.42',
    packageId: 'SyncSql.Cli', version: '2026.9.26.42',
    context: { eventName: 'push', ref: 'refs/heads/main', sha: 'tested-sha', repo: { owner: 'owner', repo: 'repo' } }
  };
}

for (const [eventName, ref] of [
  ['pull_request', 'refs/pull/69/merge'], ['workflow_dispatch', 'refs/heads/main'],
  ['push', 'refs/heads/feature'], ['push', 'refs/tags/cli-v2026.9.26.42']
]) {
  test(`rejects ${eventName} on ${ref} before writing anything`, async () => {
    const f = fixture();
    Object.assign(f.context, { eventName, ref });
    await assert.rejects(reserveTag(f), /only allowed/);
    await assert.rejects(publishRelease(f), /only allowed/);
    assert.deepEqual(f.calls, []);
  });
}

test('tags the tested commit, uploads to a draft, then publishes; reruns are idempotent', async () => {
  const f = fixture();
  await reserveTag(f);
  await publishRelease(f);
  assert.deepEqual(f.calls.map(([name]) => name), ['tag', 'draft', 'upload', 'publish']);
  assert.equal(f.calls[0][1].sha, 'tested-sha');
  assert.equal(f.calls[1][1].target_commitish, 'tested-sha');
  assert.equal(f.calls[1][1].generate_release_notes, true);
  assert.equal(f.calls[2][1].data.toString(), 'package bytes');
  assert.equal(f.calls[3][1].draft, false);
  await reserveTag(f);
  await publishRelease(f);
  assert.equal(f.calls.length, 4);
});

test('rejects a tag pointing elsewhere rather than moving it', async () => {
  const f = fixture();
  f.state.tag = { object: { type: 'commit', sha: 'different-sha' } };
  await assert.rejects(reserveTag(f), /tested main commit/);
  await assert.rejects(publishRelease(f), /tested main commit/);
  assert.deepEqual(f.calls, []);
});

test('requires a reserved tag before creating a release', async () => {
  const f = fixture();
  await assert.rejects(publishRelease(f), /must be reserved/);
  assert.deepEqual(f.calls, []);
});

test('propagates API authorization errors instead of treating them as missing tags', async () => {
  const f = fixture();
  f.github.rest.git.getRef = async () => { throw Object.assign(new Error('Forbidden'), { status: 403 }); };
  await assert.rejects(reserveTag(f), /Forbidden/);
  assert.deepEqual(f.calls, []);
});

test('an upload failure leaves a draft that can be completed on retry', async () => {
  const f = fixture();
  await reserveTag(f);
  const upload = f.github.rest.repos.uploadReleaseAsset;
  f.github.rest.repos.uploadReleaseAsset = async () => { throw new Error('Upload failed'); };
  await assert.rejects(publishRelease(f), /Upload failed/);
  assert.equal(f.state.release.draft, true);
  f.github.rest.repos.uploadReleaseAsset = upload;
  await publishRelease(f);
  assert.equal(f.state.release.draft, false);
  assert.equal(f.calls.filter(([name]) => name === 'draft').length, 1);
});

test('replaces an incomplete asset before publishing a recovered draft', async () => {
  const f = fixture();
  await reserveTag(f);
  f.state.release = { id: 7, draft: true };
  f.state.assets = [{ id: 8, name: 'SyncSql.Cli.2026.9.26.42.nupkg', state: 'starter' }];
  await publishRelease(f);
  assert.deepEqual(f.calls.map(([name]) => name), ['tag', 'delete', 'upload', 'publish']);
});
