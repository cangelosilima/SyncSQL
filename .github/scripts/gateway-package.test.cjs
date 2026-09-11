const test = require('node:test');
const assert = require('node:assert/strict');
const { imageNames, requirePrivatePackage } = require('./gateway-package.cjs');

test('image destinations are fixed to the repository owner and normalize GHCR casing', () => {
  assert.deepEqual(imageNames('ExampleOwner', '19.3'), {
    packageName: 'syncsql-oracle-gateway',
    image: 'ghcr.io/exampleowner/syncsql-oracle-gateway:19.3',
  });
});

test('rejects invalid tags and owner names before creating outputs or shell arguments', () => {
  for (const tag of ['', '-flag', 'tag\ninjected=yes', '$(command)', 'v/1', 'x'.repeat(129)]) {
    assert.throws(() => imageNames('owner', tag));
  }
  assert.throws(() => imageNames('owner/other', '19.3'));
});

test('private packages are accepted for user and organization owners', async () => {
  for (const ownerType of ['User', 'Organization']) {
    let requested;
    const github = { request: async (route) => { requested = route; return { data: { visibility: 'private' } }; } };
    assert.equal(await requirePrivatePackage(github, 'owner', ownerType), true);
    assert.match(requested, ownerType === 'User' ? /users/ : /orgs/);
  }
});

test('refuses public, internal, and unrecognized package visibility', async () => {
  for (const visibility of ['public', 'internal', undefined]) {
    const github = { request: async () => ({ data: { visibility } }) };
    await assert.rejects(requirePrivatePackage(github, 'owner', 'User', { allowMissing: true }), /private/);
  }
});

test('only an explicit 404 can permit bootstrapping a missing package', async () => {
  const github = { request: async () => { throw Object.assign(new Error('missing'), { status: 404 }); } };
  assert.equal(await requirePrivatePackage(github, 'owner', 'User', { allowMissing: true }), false);
  await assert.rejects(requirePrivatePackage(github, 'owner', 'User'), /missing/);
});

test('authentication and service failures never authorize publishing', async () => {
  for (const status of [401, 403, 429, 500]) {
    const github = { request: async () => { throw Object.assign(new Error('API failure'), { status }); } };
    await assert.rejects(requirePrivatePackage(github, 'owner', 'User', { allowMissing: true }), /API failure/);
  }
});

test('post-bootstrap checks retry metadata propagation, but not public visibility', async () => {
  let attempts = 0;
  const github = { request: async () => {
    if (++attempts < 3) throw Object.assign(new Error('missing'), { status: 404 });
    return { data: { visibility: 'private' } };
  } };
  assert.equal(await requirePrivatePackage(github, 'owner', 'User', { attempts: 3, pause: async () => {} }), true);
  assert.equal(attempts, 3);
  attempts = 0;
  github.request = async () => { attempts++; return { data: { visibility: 'public' } }; };
  await assert.rejects(requirePrivatePackage(github, 'owner', 'User', { attempts: 3, pause: async () => {} }), /private/);
  assert.equal(attempts, 1);
});
