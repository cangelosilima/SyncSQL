// @vitest-environment node
import { createHash } from 'node:crypto'
import { mkdtemp, mkdir, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const required = ['config.json', 'LICENSE.txt', 'special_tokens_map.json', 'tokenizer.json', 'tokenizer_config.json', 'vocab.txt', 'onnx/model_quantized.onnx']
let root: string
let source: string
let output: string
let manifest: { schemaVersion: number; model: string; revision: string; files: { path: string; size: number; sha256: string }[] }
const originalArgv = process.argv
const originalExitCode = process.exitCode

beforeEach(async () => {
  root = await mkdtemp(path.join(tmpdir(), 'syncsql-model-coverage-'))
  source = path.join(root, 'source')
  output = path.join(root, 'dist')
  await mkdir(path.join(source, 'onnx'), { recursive: true })
  manifest = {
    schemaVersion: 1,
    model: 'all-MiniLM-L6-v2',
    revision: 'a'.repeat(40),
    files: required.map((file) => {
      const bytes = file.endsWith('.onnx') ? 'x'.repeat(2048) : '{}\n'
      return { path: file, size: Buffer.byteLength(bytes), sha256: createHash('sha256').update(bytes).digest('hex') }
    }),
  }
  for (const file of required) await writeFile(path.join(source, file), file.endsWith('.onnx') ? 'x'.repeat(2048) : '{}\n')
  await writeFile(path.join(source, 'ATTRIBUTION.md'), 'fixture attribution')
  vi.stubEnv('AI_MODEL_SOURCE_ROOT', source)
  vi.stubEnv('AI_MODEL_OUTPUT_ROOT', output)
  vi.stubEnv('AI_MODEL_STRICT', '')
  vi.spyOn(console, 'log').mockImplementation(() => {})
  vi.spyOn(console, 'warn').mockImplementation(() => {})
  vi.spyOn(console, 'error').mockImplementation(() => {})
  process.exitCode = 0
})

afterEach(async () => {
  process.argv = originalArgv
  process.exitCode = originalExitCode
  vi.unstubAllEnvs()
  vi.restoreAllMocks()
  vi.doUnmock('node:fs/promises')
  await rm(root, { recursive: true, force: true })
})

async function run(args: string[] = [], saveManifest = true) {
  if (saveManifest) await writeFile(path.join(source, 'manifest.json'), JSON.stringify(manifest))
  process.argv = [...originalArgv.slice(0, 2), ...args]
  vi.resetModules()
  const script = './package-ai-model.mjs'
  await import(script)
}

async function capability() {
  return JSON.parse(await readFile(path.join(output, 'data', 'ai-capabilities.json'), 'utf8')).filterGenerator
}

describe('model verification and packaging outcomes', () => {
  it('copies all verified assets and supplemental files byte for byte', async () => {
    await run()
    expect(await capability()).toEqual({ available: true, model: manifest.model, version: 1 })
    for (const file of [...required, 'manifest.json', 'ATTRIBUTION.md']) {
      expect(await readFile(path.join(output, 'models', manifest.model, file))).toEqual(await readFile(path.join(source, file)))
    }
    expect(process.exitCode).toBe(0)
  })

  it('verifies without creating output', async () => {
    await run(['--strict', '--verify-only'])
    await expect(readFile(path.join(output, 'data', 'ai-capabilities.json'))).rejects.toMatchObject({ code: 'ENOENT' })
    expect(process.exitCode).toBe(0)
  })

  it.each(['missing', 'invalid'])('reports a %s manifest', async (kind) => {
    if (kind === 'invalid') await writeFile(path.join(source, 'manifest.json'), '{')
    await run([], false)
    expect(await capability()).toEqual({ available: false, reason: 'model-missing', version: 1 })
    expect(console.warn).toHaveBeenCalledWith(expect.stringContaining('missing or invalid'))
  })

  it.each([
    { schemaVersion: 2 }, { model: 'other' }, { revision: 123 }, { revision: 'invalid' }, { files: null },
  ])('rejects unsupported manifest shape %j', async (change) => {
    Object.assign(manifest, change)
    await run()
    expect(console.warn).toHaveBeenCalledWith(expect.stringContaining('unsupported shape'))
    expect((await capability()).reason).toBe('model-missing')
  })

  it.each(['duplicate', 'missing'])('rejects %s required entries', async (kind) => {
    if (kind === 'duplicate') manifest.files.push(manifest.files[0])
    else manifest.files.pop()
    await run()
    expect(console.warn).toHaveBeenCalledWith(expect.stringContaining('exactly once'))
  })

  it.each([null, { path: 3 }, { size: 1.5 }, { size: -1 }, { sha256: 1 }, { sha256: 'invalid' }])('rejects malformed extra entries %j', async (change) => {
    manifest.files.push(change === null ? null! : { ...manifest.files[0], path: 'extra', ...change } as typeof manifest.files[number])
    await run()
    expect(console.warn).toHaveBeenCalledWith(expect.stringContaining('invalid file entry'))
  })

  it.each(['', '../outside', 'nested/../../outside'])('rejects unsafe path %j', async (file) => {
    manifest.files.push({ ...manifest.files[0], path: file })
    await run()
    expect((await capability()).reason).toBe('packaging-failed')
    expect(console.warn).toHaveBeenCalledWith(expect.stringContaining('unsafe file path'))
  })

  it.each(['missing', 'directory'])('reports a %s asset', async (kind) => {
    await rm(path.join(source, 'config.json'))
    if (kind === 'directory') await mkdir(path.join(source, 'config.json'))
    await run()
    expect((await capability()).reason).toBe('model-missing')
    expect(console.warn).toHaveBeenCalledWith(expect.stringContaining(kind === 'missing' ? 'config.json is missing' : 'config.json is not a file'))
  })

  it('detects unresolved LFS before checking its checksum', async () => {
    await writeFile(path.join(source, required[6]), 'version https://git-lfs.github.com/spec/v1\n')
    await run()
    expect((await capability()).reason).toBe('lfs-unresolved')
  })

  it.each(['same size', 'different size'])('rejects corrupt assets of %s', async (kind) => {
    await writeFile(path.join(source, 'config.json'), kind === 'same size' ? '[]\n' : 'corrupt')
    await run(['--strict'])
    expect(process.exitCode).toBe(1)
    expect(console.error).toHaveBeenCalledWith(expect.stringContaining('checksum-mismatch'))
    expect((await capability()).available).toBe(false)
  })

  it('honors strict mode from the environment', async () => {
    vi.stubEnv('AI_MODEL_STRICT', '1')
    await run(['--verify-only'], false)
    expect(process.exitCode).toBe(1)
  })

  it('uses repository defaults when no environment paths are set', async () => {
    vi.stubEnv('AI_MODEL_SOURCE_ROOT', undefined)
    vi.stubEnv('AI_MODEL_OUTPUT_ROOT', undefined)
    const read = vi.fn().mockRejectedValue(new Error('missing model'))
    vi.doMock('node:fs/promises', async () => ({
      ...await vi.importActual<typeof import('node:fs/promises')>('node:fs/promises'),
      readFile: read,
    }))
    await run(['--verify-only'], false)
    expect(read).toHaveBeenCalledWith(path.resolve('vendor/ai/all-MiniLM-L6-v2/manifest.json'), 'utf8')
    expect(console.warn).toHaveBeenCalledWith(expect.stringContaining('model-missing'))
  })

  it.each([false, true])('removes partial copies when packaging fails (strict=%s)', async (strict) => {
    await rm(path.join(source, 'ATTRIBUTION.md'))
    await run(strict ? ['--strict'] : [])
    expect((await capability()).reason).toBe('packaging-failed')
    await expect(readFile(path.join(output, 'models', manifest.model, 'config.json'))).rejects.toMatchObject({ code: 'ENOENT' })
    expect(process.exitCode).toBe(strict ? 1 : 0)
  })

  it.each([new Error('disk full'), 'disk full'])('reports capability write failures: %s', async (failure) => {
    vi.doMock('node:fs/promises', async () => {
      const actual = await vi.importActual<typeof import('node:fs/promises')>('node:fs/promises')
      return { ...actual, writeFile: vi.fn().mockRejectedValue(failure) }
    })
    await run(['--strict'])
    expect(process.exitCode).toBe(1)
    expect(console.error).toHaveBeenCalledWith(expect.stringContaining('Capability manifest could not be written: disk full'))
  })
})
