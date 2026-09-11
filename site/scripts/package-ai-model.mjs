import { createHash } from 'node:crypto'
import { createReadStream } from 'node:fs'
import { copyFile, mkdir, readFile, rm, stat, writeFile } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const scriptDir = path.dirname(fileURLToPath(import.meta.url))
const siteRoot = path.resolve(scriptDir, '..')
const sourceRoot = process.env.AI_MODEL_SOURCE_ROOT
  ? path.resolve(process.env.AI_MODEL_SOURCE_ROOT)
  : path.join(siteRoot, 'vendor', 'ai', 'all-MiniLM-L6-v2')
const manifestPath = path.join(sourceRoot, 'manifest.json')
const outputRoot = process.env.AI_MODEL_OUTPUT_ROOT
  ? path.resolve(process.env.AI_MODEL_OUTPUT_ROOT)
  : path.join(siteRoot, 'dist')
const outputModelRoot = path.join(outputRoot, 'models', 'all-MiniLM-L6-v2')
const capabilityPath = path.join(outputRoot, 'data', 'ai-capabilities.json')
const strict = process.argv.includes('--strict') || process.env.AI_MODEL_STRICT === '1'
const verifyOnly = process.argv.includes('--verify-only')

const REASON = {
  missing: 'model-missing',
  pointer: 'lfs-unresolved',
  checksum: 'checksum-mismatch',
  packaging: 'packaging-failed',
}
const REQUIRED_FILES = new Set([
  'config.json',
  'LICENSE.txt',
  'special_tokens_map.json',
  'tokenizer.json',
  'tokenizer_config.json',
  'vocab.txt',
  'onnx/model_quantized.onnx',
])

async function sha256(filePath) {
  const hash = createHash('sha256')
  for await (const chunk of createReadStream(filePath)) hash.update(chunk)
  return hash.digest('hex')
}

async function isLfsPointer(filePath) {
  const handle = await readFile(filePath)
  if (handle.length > 1024) return false
  return handle.toString('utf8').startsWith('version https://git-lfs.github.com/spec/v1')
}

async function verifyModel() {
  let manifest
  try {
    manifest = JSON.parse(await readFile(manifestPath, 'utf8'))
  } catch {
    return { ok: false, reason: REASON.missing, detail: 'manifest.json is missing or invalid' }
  }

  if (
    manifest.schemaVersion !== 1 ||
    manifest.model !== 'all-MiniLM-L6-v2' ||
    typeof manifest.revision !== 'string' ||
    !/^[a-f0-9]{40}$/i.test(manifest.revision) ||
    !Array.isArray(manifest.files)
  ) {
    return { ok: false, reason: REASON.missing, detail: 'manifest.json has an unsupported shape' }
  }

  const listedFiles = new Set(manifest.files.map((entry) => entry?.path))
  if (listedFiles.size !== manifest.files.length || [...REQUIRED_FILES].some((file) => !listedFiles.has(file))) {
    return {
      ok: false,
      reason: REASON.missing,
      detail: 'manifest.json does not list every required model file exactly once',
    }
  }

  for (const entry of manifest.files) {
    if (
      !entry ||
      typeof entry.path !== 'string' ||
      !Number.isSafeInteger(entry.size) ||
      entry.size < 0 ||
      typeof entry.sha256 !== 'string' ||
      !/^[a-f0-9]{64}$/i.test(entry.sha256)
    ) {
      return { ok: false, reason: REASON.missing, detail: 'manifest.json contains an invalid file entry' }
    }
    const filePath = path.resolve(sourceRoot, ...entry.path.split('/'))
    const relativePath = path.relative(sourceRoot, filePath)
    if (!relativePath || relativePath.startsWith('..') || path.isAbsolute(relativePath)) {
      return { ok: false, reason: REASON.packaging, detail: 'manifest.json contains an unsafe file path' }
    }
    let fileStat
    try {
      fileStat = await stat(filePath)
    } catch {
      return { ok: false, reason: REASON.missing, detail: `${entry.path} is missing` }
    }
    if (!fileStat.isFile()) {
      return { ok: false, reason: REASON.missing, detail: `${entry.path} is not a file` }
    }
    if (await isLfsPointer(filePath)) {
      return { ok: false, reason: REASON.pointer, detail: `${entry.path} is an unresolved Git LFS pointer` }
    }
    if (fileStat.size !== entry.size || (await sha256(filePath)) !== entry.sha256) {
      return { ok: false, reason: REASON.checksum, detail: `${entry.path} failed size or checksum verification` }
    }
  }

  return { ok: true, manifest }
}

async function writeCapability(available, details) {
  await mkdir(path.dirname(capabilityPath), { recursive: true })
  const filterGenerator = available
    ? { available: true, model: details.manifest.model, version: 1 }
    : { available: false, reason: details.reason ?? REASON.packaging, version: 1 }
  await writeFile(capabilityPath, `${JSON.stringify({ filterGenerator }, null, 2)}\n`, 'utf8')
}

async function packageModel(result) {
  await rm(outputModelRoot, { recursive: true, force: true })
  if (!result.ok) {
    await writeCapability(false, result)
    return
  }

  await mkdir(outputModelRoot, { recursive: true })
  for (const entry of result.manifest.files) {
    const source = path.join(sourceRoot, ...entry.path.split('/'))
    const destination = path.join(outputModelRoot, ...entry.path.split('/'))
    await mkdir(path.dirname(destination), { recursive: true })
    await copyFile(source, destination)
  }
  for (const supplemental of ['manifest.json', 'ATTRIBUTION.md']) {
    await copyFile(path.join(sourceRoot, supplemental), path.join(outputModelRoot, supplemental))
  }
  await writeCapability(true, result)
}

const result = await verifyModel()

if (!result.ok) {
  const message = `AI model unavailable (${result.reason}): ${result.detail}`
  if (strict) {
    console.error(message)
    process.exitCode = 1
  } else {
    console.warn(`WARNING: ${message}. The core site will be published with AI disabled.`)
  }
} else {
  console.log(`AI model verified: ${result.manifest.model} @ ${result.manifest.revision}`)
}

if (!verifyOnly) {
  try {
    await packageModel(result)
  } catch (error) {
    await rm(outputModelRoot, { recursive: true, force: true })
    let capabilityError = ''
    try {
      await writeCapability(false, { reason: REASON.packaging })
    } catch (writeError) {
      capabilityError = ` Capability manifest could not be written: ${writeError instanceof Error ? writeError.message : String(writeError)}.`
    }
    const message = `AI model packaging failed: ${error instanceof Error ? error.message : String(error)}.${capabilityError}`
    if (strict) {
      console.error(message)
      process.exitCode = 1
    } else {
      console.warn(`WARNING: ${message}. The core site will be published with AI disabled.`)
    }
  }
}
