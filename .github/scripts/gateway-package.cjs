const packageName = 'syncsql-oracle-gateway';

function imageNames(owner, tag) {
  if (!/^[a-zA-Z0-9][a-zA-Z0-9-]*$/.test(owner)) throw new Error('Invalid package owner');
  if (!/^[a-zA-Z0-9_][a-zA-Z0-9_.-]{0,127}$/.test(tag)) throw new Error('Invalid image tag');
  return { packageName, image: `ghcr.io/${owner.toLowerCase()}/${packageName}:${tag}` };
}

// GHCR has no Docker push flag for private visibility. New packages default to
// private; the publisher first creates an empty package and verifies its actual
// visibility before uploading any Oracle image layers.
async function requirePrivatePackage(github, owner, ownerType, options = {}) {
  imageNames(owner, 'validation');
  if (!['User', 'Organization'].includes(ownerType)) throw new Error('Unknown package owner type');
  const { allowMissing = false, attempts = 1, pause = () => new Promise(resolve => setTimeout(resolve, 5000)) } = options;
  const route = ownerType === 'Organization'
    ? 'GET /orgs/{org}/packages/container/{package_name}'
    : 'GET /users/{username}/packages/container/{package_name}';
  for (let attempt = 1; attempt <= attempts; attempt++) {
    let response;
    try {
      response = await github.request(route, {
        org: owner, username: owner, package_name: packageName,
      });
    } catch (error) {
      if (error.status !== 404) throw error;
      if (attempt < attempts) { await pause(); continue; }
      if (allowMissing) return false;
      throw error;
    }
    if (response.data.visibility !== 'private') {
      throw new Error(`Refusing to publish Oracle layers: ${owner}/${packageName} must be private (received ${response.data.visibility}).`);
    }
    return true;
  }
  throw new Error('Could not verify package visibility');
}

module.exports = { imageNames, requirePrivatePackage };
