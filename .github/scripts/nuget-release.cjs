const fs = require('node:fs');
const path = require('node:path');

function requireMainPush(context) {
  if (context.eventName !== 'push' || context.ref !== 'refs/heads/main') {
    throw new Error('Publishing is only allowed for a push to main.');
  }
}

async function findTag({ github, context, tag }) {
  try {
    const { data } = await github.rest.git.getRef({ ...context.repo, ref: `tags/${tag}` });
    if (data.object.type !== 'commit' || data.object.sha !== context.sha) {
      throw new Error(`Tag ${tag} does not point to the tested main commit.`);
    }
    return data;
  } catch (error) {
    if (error.status === 404) return null;
    throw error;
  }
}

async function reserveTag(options) {
  const { github, context, tag } = options;
  requireMainPush(context);
  if (!await findTag(options)) {
    await github.rest.git.createRef({
      ...context.repo, ref: `refs/tags/${tag}`, sha: context.sha
    });
  }
}

async function publishRelease(options) {
  const { github, context, tag, packageId, version, artifacts = 'artifacts' } = options;
  requireMainPush(context);
  if (!await findTag(options)) throw new Error('The release tag must be reserved before publishing.');
  const name = `${packageId}.${version}.nupkg`;
  const data = fs.readFileSync(path.join(artifacts, name));
  let release;
  try {
    ({ data: release } = await github.rest.repos.getReleaseByTag({ ...context.repo, tag }));
  } catch (error) {
    if (error.status !== 404) throw error;
    ({ data: release } = await github.rest.repos.createRelease({
      ...context.repo, tag_name: tag, target_commitish: context.sha,
      name: `${packageId} ${version}`, draft: true, prerelease: false,
      generate_release_notes: true
    }));
  }
  const assets = await github.paginate(github.rest.repos.listReleaseAssets, {
    ...context.repo, release_id: release.id, per_page: 100
  });
  const asset = assets.find(item => item.name === name);
  if (asset && asset.state !== 'uploaded') {
    await github.rest.repos.deleteReleaseAsset({ ...context.repo, asset_id: asset.id });
  }
  if (!asset || asset.state !== 'uploaded') {
    await github.rest.repos.uploadReleaseAsset({
      ...context.repo, release_id: release.id, name, data,
      headers: { 'content-type': 'application/octet-stream', 'content-length': data.length }
    });
  }
  if (release.draft) {
    await github.rest.repos.updateRelease({
      ...context.repo, release_id: release.id, draft: false, make_latest: 'false'
    });
  }
}

module.exports = { requireMainPush, reserveTag, publishRelease };
