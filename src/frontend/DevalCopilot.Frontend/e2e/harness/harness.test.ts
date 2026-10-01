import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { existsSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import { after, describe, it } from 'node:test'
import { fileURLToPath } from 'node:url'
import { getAuthorizedJson } from './authorizedRequest.ts'
import {
  createOwnedRoot,
  DATABASE_FILE,
  MARKER_FILE,
  removeOwnedDatabase,
  removeOwnedRoot,
  resolveRunRoot,
  ROOT_ENV,
  ROOT_PREFIX,
  TOKEN_ENV,
  verifyOwnedRoot,
} from './ownedRoot.ts'

// Every fixture below is created by this file under the OS temp directory and removed in `after`; nothing outside those
// directories is touched, including by the negative tests (which only prove that files are PRESERVED).

const resetScript = join(dirname(fileURLToPath(import.meta.url)), '..', 'reset-smoke-db.mjs')
const created: string[] = []

function track<T extends string>(path: T): T {
  created.push(path)
  return path
}

function writeDatabaseFiles(directory: string): string[] {
  const files = ['', '-wal', '-shm'].map((suffix) => join(directory, `${DATABASE_FILE}${suffix}`))
  files.forEach((file) => writeFileSync(file, 'fixture'))
  return files
}

function newOwnedRoot() {
  const owned = createOwnedRoot()
  track(owned.root)
  return owned
}

after(() => {
  for (const path of created) {
    rmSync(path, { recursive: true, force: true })
  }
})

describe('request sanitization', () => {
  const SYNTHETIC_TOKEN = 'synthetic-secret-token-0123456789abcdef'

  function leaks(error: unknown): string[] {
    const e = error as Error & { cause?: unknown }
    return [
      String(e),
      e.message,
      e.stack ?? '',
      JSON.stringify(e),
      JSON.stringify(Object.getOwnPropertyNames(e).map((name) => (e as unknown as Record<string, unknown>)[name])),
      String(e.cause ?? ''),
    ].filter((text) => text.includes(SYNTHETIC_TOKEN))
  }

  it('does not retain the credential when the transport fails with the full request in its message, cause and stack', async () => {
    const transportFailure = new Error(`apiRequestContext.get: socket hang up\nCall log:\n  - Authorization: Bearer ${SYNTHETIC_TOKEN}`, {
      cause: new Error(`Bearer ${SYNTHETIC_TOKEN}`),
    })
    transportFailure.stack = `${transportFailure.message}\n    at Bearer ${SYNTHETIC_TOKEN}`
    const request = { get: () => Promise.reject(transportFailure) }

    const error = await getAuthorizedJson(request, 'http://127.0.0.1:1/x', SYNTHETIC_TOKEN, 'Run summaries').then(
      () => assert.fail('expected a failure'),
      (caught: unknown) => caught,
    )

    assert.deepEqual(leaks(error), [])
    assert.equal((error as Error).message, 'Run summaries request failed before a response was received.')
    assert.equal((error as Error & { cause?: unknown }).cause, undefined)
  })

  it('reports a refusal by status only and never echoes the response', async () => {
    const request = {
      get: () => Promise.resolve({ ok: () => false, status: () => 401, json: () => Promise.resolve({ echoed: SYNTHETIC_TOKEN }) }),
    }

    const error = await getAuthorizedJson(request, 'u', SYNTHETIC_TOKEN, 'Run summaries').catch((caught: unknown) => caught)

    assert.deepEqual(leaks(error), [])
    assert.match((error as Error).message, /status 401/)
  })

  it('does not retain the credential when the body cannot be read, and returns the body on success', async () => {
    const unreadable = { get: () => Promise.resolve({ ok: () => true, status: () => 200, json: () => Promise.reject(new Error(SYNTHETIC_TOKEN)) }) }
    const error = await getAuthorizedJson(unreadable, 'u', SYNTHETIC_TOKEN, 'Run summaries').catch((caught: unknown) => caught)
    assert.deepEqual(leaks(error), [])

    let sentHeader = ''
    const ok = {
      get: (_url: string, options: { headers: Record<string, string> }) => {
        sentHeader = options.headers.Authorization
        return Promise.resolve({ ok: () => true, status: () => 200, json: () => Promise.resolve([{ projectName: 'p' }]) })
      },
    }
    assert.deepEqual(await getAuthorizedJson(ok, 'u', SYNTHETIC_TOKEN, 'Run summaries'), [{ projectName: 'p' }])
    assert.equal(sentHeader, `Bearer ${SYNTHETIC_TOKEN}`) // normal authentication is kept
  })
})

describe('owned smoke database cleanup', () => {
  it('removes the database and its WAL and SHM files inside a verified owned root, and nothing else', () => {
    const owned = newOwnedRoot()
    const files = writeDatabaseFiles(owned.root)
    const sibling = join(owned.root, 'repos', 'keep.txt')
    writeFileSync(sibling, 'keep')

    removeOwnedDatabase(owned.root, owned.token, join(owned.root, DATABASE_FILE))

    files.forEach((file) => assert.equal(existsSync(file), false))
    assert.equal(existsSync(sibling), true)
    assert.equal(existsSync(join(owned.root, MARKER_FILE)), true)
  })

  it('removes the whole root only when ownership verifies', () => {
    const owned = newOwnedRoot()
    writeDatabaseFiles(owned.root)

    removeOwnedRoot(owned.root, owned.token)

    assert.equal(existsSync(owned.root), false)
  })

  it('refuses a database path outside the owned root and preserves those files', () => {
    const owned = newOwnedRoot()
    const outside = track(mkdtempSync(join(tmpdir(), 'devalcopilot-e2e-test-outside-')))
    const outsideFiles = writeDatabaseFiles(outside)

    assert.throws(() => removeOwnedDatabase(owned.root, owned.token, join(outside, DATABASE_FILE)), /outside the owned/)
    assert.throws(
      () => removeOwnedDatabase(owned.root, owned.token, join(owned.root, '..', 'x', DATABASE_FILE).replace('x', outside.split(/[\\/]/).pop()!)),
      /outside the owned/,
    )
    outsideFiles.forEach((file) => assert.equal(existsSync(file), true))
  })

  it('refuses invalid paths: missing, relative, the root itself, a nested directory, or another file name', () => {
    const owned = newOwnedRoot()
    const nested = join(owned.root, 'repos')
    writeDatabaseFiles(nested)
    const other = join(owned.root, 'other.db')
    writeFileSync(other, 'fixture')

    for (const candidate of [undefined, '', DATABASE_FILE, owned.root, join(nested, DATABASE_FILE), other]) {
      assert.throws(() => removeOwnedDatabase(owned.root, owned.token, candidate))
    }
    assert.equal(existsSync(join(nested, DATABASE_FILE)), true)
    assert.equal(existsSync(other), true)
  })

  it('refuses a matching name without the ownership marker', () => {
    const imposter = track(mkdtempSync(join(tmpdir(), ROOT_PREFIX)))
    const files = writeDatabaseFiles(imposter)

    assert.throws(() => removeOwnedDatabase(imposter, 'any-token', join(imposter, DATABASE_FILE)), /ownership marker/)
    assert.throws(() => removeOwnedRoot(imposter, 'any-token'), /ownership marker/)
    files.forEach((file) => assert.equal(existsSync(file), true))
  })

  it('refuses a marker that belongs to another run', () => {
    const owned = newOwnedRoot()
    const files = writeDatabaseFiles(owned.root)

    assert.throws(() => removeOwnedDatabase(owned.root, 'a-different-token', join(owned.root, DATABASE_FILE)), /ownership marker/)
    assert.throws(() => removeOwnedDatabase(owned.root, undefined, join(owned.root, DATABASE_FILE)), /missing/)
    files.forEach((file) => assert.equal(existsSync(file), true))
  })

  it('refuses a root that is not directly under the temp directory, even with a valid marker', () => {
    const parent = track(mkdtempSync(join(tmpdir(), 'devalcopilot-e2e-test-parent-')))
    const nestedRoot = join(parent, `${ROOT_PREFIX}nested`)
    mkdirSync(nestedRoot)
    writeFileSync(join(nestedRoot, MARKER_FILE), 'token')
    const files = writeDatabaseFiles(nestedRoot)

    assert.throws(() => verifyOwnedRoot(nestedRoot, 'token'), /directly under the temp directory/)
    assert.throws(() => removeOwnedDatabase(nestedRoot, 'token', join(nestedRoot, DATABASE_FILE)))
    files.forEach((file) => assert.equal(existsSync(file), true))
  })

  it('refuses a root without the harness prefix or one that does not exist', () => {
    const wrongName = track(mkdtempSync(join(tmpdir(), 'unrelated-')))
    writeFileSync(join(wrongName, MARKER_FILE), 'token')
    assert.throws(() => verifyOwnedRoot(wrongName, 'token'))
    assert.throws(() => verifyOwnedRoot(join(tmpdir(), `${ROOT_PREFIX}does-not-exist`), 'token'))
  })
})

describe('run root resolution', () => {
  it('lets a worker reuse a verified root and fails closed on an unverified inherited one', () => {
    const owned = newOwnedRoot()
    assert.equal(resolveRunRoot({ [ROOT_ENV]: owned.root, [TOKEN_ENV]: owned.token }, true).root, owned.root)
    assert.throws(() => resolveRunRoot({ [ROOT_ENV]: owned.root, [TOKEN_ENV]: 'wrong' }, true))
    assert.throws(() => resolveRunRoot({ [ROOT_ENV]: owned.root }, true))
    assert.throws(() => resolveRunRoot({}, true))
  })

  it('makes the orchestrator create a fresh root and ignore an inherited one', () => {
    const inherited = newOwnedRoot()
    const orchestrated = resolveRunRoot({ [ROOT_ENV]: inherited.root, [TOKEN_ENV]: inherited.token }, false)
    track(orchestrated.root)

    assert.notEqual(orchestrated.root, inherited.root)
    assert.equal(verifyOwnedRoot(orchestrated.root, orchestrated.token).root, orchestrated.root)
    // A second load in the same process reuses the same fresh root instead of orphaning one.
    assert.equal(resolveRunRoot({}, false).root, orchestrated.root)
  })
})

describe('the reset script', () => {
  function run(env: Record<string, string | undefined>) {
    return spawnSync(process.execPath, [resetScript], { env: { PATH: process.env.PATH, ...env }, encoding: 'utf8' })
  }

  it('deletes the owned database and leaves a stray database in the working directory alone', () => {
    const owned = newOwnedRoot()
    const files = writeDatabaseFiles(owned.root)
    const cwd = track(mkdtempSync(join(tmpdir(), 'devalcopilot-e2e-test-cwd-')))
    const strayFiles = writeDatabaseFiles(cwd)

    const result = spawnSync(process.execPath, [resetScript], {
      cwd,
      env: { PATH: process.env.PATH, [ROOT_ENV]: owned.root, [TOKEN_ENV]: owned.token, DEVALCOPILOT_SMOKE_DB_PATH: join(owned.root, DATABASE_FILE) },
      encoding: 'utf8',
    })

    assert.equal(result.status, 0, result.stderr)
    files.forEach((file) => assert.equal(existsSync(file), false))
    strayFiles.forEach((file) => assert.equal(existsSync(file), true))
  })

  it('refuses an arbitrary path, a missing path, and a missing ownership, preserving the files', () => {
    const owned = newOwnedRoot()
    const outside = track(mkdtempSync(join(tmpdir(), 'devalcopilot-e2e-test-outside-')))
    const outsideFiles = writeDatabaseFiles(outside)
    const ownedFiles = writeDatabaseFiles(owned.root)
    const cwd = track(mkdtempSync(join(tmpdir(), 'devalcopilot-e2e-test-cwd-')))
    const cwdFiles = writeDatabaseFiles(cwd)

    const arbitrary = run({ [ROOT_ENV]: owned.root, [TOKEN_ENV]: owned.token, DEVALCOPILOT_SMOKE_DB_PATH: join(outside, DATABASE_FILE) })
    const noPath = spawnSync(process.execPath, [resetScript], { cwd, env: { PATH: process.env.PATH, [ROOT_ENV]: owned.root, [TOKEN_ENV]: owned.token }, encoding: 'utf8' })
    const noOwnership = run({ DEVALCOPILOT_SMOKE_DB_PATH: join(outside, DATABASE_FILE) })
    const wrongToken = run({ [ROOT_ENV]: owned.root, [TOKEN_ENV]: 'nope', DEVALCOPILOT_SMOKE_DB_PATH: join(owned.root, DATABASE_FILE) })

    for (const result of [arbitrary, noPath, noOwnership, wrongToken]) {
      assert.notEqual(result.status, 0)
      assert.match(result.stderr, /Refusing to reset/)
    }
    ;[...outsideFiles, ...ownedFiles, ...cwdFiles].forEach((file) => assert.equal(existsSync(file), true))
  })
})
