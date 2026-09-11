import { createHash } from 'node:crypto'
import { execFileSync, spawnSync } from 'node:child_process'
import { existsSync, mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { describe, expect, it } from 'vitest'

const script = path.resolve('scripts/package-ai-model.mjs')

describe('optional AI model packaging', () => {
  it.each(['true', 'false'])('preserves vendored checksums with core.autocrlf=%s', (autocrlf) => {
    const modelPath = 'site/vendor/ai/all-MiniLM-L6-v2'
    const manifest = JSON.parse(readFileSync(path.resolve('..', modelPath, 'manifest.json'), 'utf8')) as {
      files: { path: string; size: number; sha256: string }[]
    }
    for (const entry of manifest.files.filter((file) => !file.path.endsWith('.onnx'))) {
      // Apply checkout filters to committed bytes using the working tree's attributes.
      // This reproduces Windows checkout conversion even when this test runs on Linux.
      const bytes = execFileSync('git', [
        '-c',
        `core.autocrlf=${autocrlf}`,
        'cat-file',
        '--filters',
        `HEAD:${modelPath}/${entry.path}`,
      ])
      expect(bytes.length, entry.path).toBe(entry.size)
      expect(createHash('sha256').update(bytes).digest('hex'), entry.path).toBe(entry.sha256)
    }
  })

  it('packages verified files and enables the capability', () => {
    const fixture = createFixture('real model bytes')
    run([], fixture.source, fixture.output)
    expect(readCapability(fixture.output).filterGenerator.available).toBe(true)
    expect(
      readFileSync(path.join(fixture.output, 'models', 'all-MiniLM-L6-v2', 'onnx', 'model_quantized.onnx'), 'utf8'),
    ).toBe('real model bytes')
  })

  it('keeps optional builds successful when the model is missing', () => {
    const root = mkdtempSync(path.join(tmpdir(), 'syncsql-ai-missing-'))
    const source = path.join(root, 'source')
    const output = path.join(root, 'dist')
    mkdirSync(source, { recursive: true })
    const staleModel = path.join(output, 'models', 'all-MiniLM-L6-v2', 'partial.onnx')
    mkdirSync(path.dirname(staleModel), { recursive: true })
    writeFileSync(staleModel, 'partial')
    run([], source, output)
    expect(readCapability(output).filterGenerator).toMatchObject({ available: false, reason: 'model-missing' })
    expect(existsSync(path.dirname(staleModel))).toBe(false)
  })

  it('fails strict verification when the model is missing', () => {
    const root = mkdtempSync(path.join(tmpdir(), 'syncsql-ai-strict-'))
    const result = spawnSync(process.execPath, [script, '--strict', '--verify-only'], {
      env: {
        ...process.env,
        AI_MODEL_SOURCE_ROOT: path.join(root, 'missing'),
        AI_MODEL_OUTPUT_ROOT: path.join(root, 'dist'),
      },
      encoding: 'utf8',
    })
    expect(result.status).not.toBe(0)
  })

  it('detects unresolved LFS pointers without failing optional packaging', () => {
    const pointer = 'version https://git-lfs.github.com/spec/v1\noid sha256:abc\nsize 123\n'
    const fixture = createFixture(pointer)
    run([], fixture.source, fixture.output)
    expect(readCapability(fixture.output).filterGenerator).toMatchObject({ available: false, reason: 'lfs-unresolved' })
    expect(runStrict(fixture.source, fixture.output).status).not.toBe(0)
  })

  it('detects checksum mismatches', () => {
    const fixture = createFixture('expected')
    writeFileSync(path.join(fixture.source, 'onnx', 'model_quantized.onnx'), 'changed')
    run([], fixture.source, fixture.output)
    expect(readCapability(fixture.output).filterGenerator).toMatchObject({
      available: false,
      reason: 'checksum-mismatch',
    })
    expect(runStrict(fixture.source, fixture.output).status).not.toBe(0)
  })

  it('disables AI and removes partial output when optional packaging fails', () => {
    const fixture = createFixture('valid weights')
    rmSync(path.join(fixture.source, 'ATTRIBUTION.md'))
    run([], fixture.source, fixture.output)
    expect(readCapability(fixture.output).filterGenerator).toMatchObject({
      available: false,
      reason: 'packaging-failed',
    })
    expect(existsSync(path.join(fixture.output, 'models', 'all-MiniLM-L6-v2'))).toBe(false)
    expect(runStrict(fixture.source, fixture.output, false).status).not.toBe(0)
  })
})

function createFixture(modelContent: string) {
  const root = mkdtempSync(path.join(tmpdir(), 'syncsql-ai-package-'))
  const source = path.join(root, 'source')
  const output = path.join(root, 'dist')
  const files: Record<string, string> = {
    'config.json': '{}',
    'LICENSE.txt': 'fixture license',
    'special_tokens_map.json': '{}',
    'tokenizer.json': '{}',
    'tokenizer_config.json': '{}',
    'vocab.txt': '[PAD]',
    'onnx/model_quantized.onnx': modelContent,
  }
  for (const [relativePath, content] of Object.entries(files)) {
    const filePath = path.join(source, ...relativePath.split('/'))
    mkdirSync(path.dirname(filePath), { recursive: true })
    writeFileSync(filePath, content)
  }
  writeFileSync(path.join(source, 'ATTRIBUTION.md'), 'fixture')
  writeFileSync(
    path.join(source, 'manifest.json'),
    JSON.stringify({
      schemaVersion: 1,
      model: 'all-MiniLM-L6-v2',
      revision: '0123456789abcdef0123456789abcdef01234567',
      files: Object.entries(files).map(([relativePath, content]) => ({
        path: relativePath,
        size: Buffer.byteLength(content),
        sha256: hash(content),
      })),
    }),
  )
  return { source, output }
}

function runStrict(source: string, output: string, verifyOnly = true) {
  return spawnSync(process.execPath, [script, '--strict', ...(verifyOnly ? ['--verify-only'] : [])], {
    env: { ...process.env, AI_MODEL_SOURCE_ROOT: source, AI_MODEL_OUTPUT_ROOT: output },
    encoding: 'utf8',
  })
}

function run(args: string[], source: string, output: string) {
  execFileSync(process.execPath, [script, ...args], {
    env: { ...process.env, AI_MODEL_SOURCE_ROOT: source, AI_MODEL_OUTPUT_ROOT: output },
    stdio: 'pipe',
  })
}

function readCapability(output: string): { filterGenerator: { available: boolean; reason?: string } } {
  return JSON.parse(readFileSync(path.join(output, 'data', 'ai-capabilities.json'), 'utf8'))
}

function hash(value: string): string {
  return createHash('sha256').update(value).digest('hex')
}
