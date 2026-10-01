import { randomBytes } from 'node:crypto'
import {
  existsSync,
  lstatSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  realpathSync,
  rmSync,
  unlinkSync,
  writeFileSync,
} from 'node:fs'
import { tmpdir } from 'node:os'
import { basename, dirname, isAbsolute, join, relative, resolve } from 'node:path'

// Ownership of the disposable end-to-end root. A directory is deleted from only if this run created it: it must sit
// directly under the OS temp directory, carry the harness prefix, and contain a marker file holding the secret token
// of the run that created it. An inherited path, a filename prefix, or a matching name alone never establishes
// ownership. Self-contained (node built-ins only) so the reset script and the tests can import it directly.

export const ROOT_PREFIX = 'devalcopilot-e2e-'
export const MARKER_FILE = '.devalcopilot-e2e-owner'
export const DATABASE_FILE = 'playwright-smoke.db'
export const ROOT_ENV = 'DEVALCOPILOT_E2E_ROOT'
export const TOKEN_ENV = 'DEVALCOPILOT_E2E_OWNER_TOKEN'

export interface OwnedRoot {
  /** The real, resolved path of the verified root. */
  root: string
  token: string
}

/** Creates a fresh root under the temp directory and records the ownership marker. */
export function createOwnedRoot(): OwnedRoot {
  const root = realpathSync(mkdtempSync(join(realpathSync(tmpdir()), ROOT_PREFIX)))
  const token = randomBytes(32).toString('hex')
  writeFileSync(join(root, MARKER_FILE), token, { flag: 'wx' })
  mkdirSync(join(root, 'repos'))
  return { root, token }
}

/** Throws unless `root` is a real directory this run created (see the module comment). Returns the resolved root. */
export function verifyOwnedRoot(root: string | undefined, token: string | undefined): OwnedRoot {
  if (!root || !token || !isAbsolute(root)) {
    throw new Error('The end-to-end root or its ownership token is missing or not absolute.')
  }
  const stat = lstatSync(root, { throwIfNoEntry: false })
  if (!stat || !stat.isDirectory() || stat.isSymbolicLink()) {
    throw new Error('The end-to-end root is not an existing real directory.')
  }
  const real = realpathSync(root)
  if (dirname(real) !== realpathSync(tmpdir()) || !basename(real).startsWith(ROOT_PREFIX)) {
    throw new Error('The end-to-end root is not a harness directory directly under the temp directory.')
  }
  const marker = join(real, MARKER_FILE)
  const markerStat = lstatSync(marker, { throwIfNoEntry: false })
  if (!markerStat || !markerStat.isFile() || markerStat.isSymbolicLink() || readFileSync(marker, 'utf8') !== token) {
    throw new Error('The end-to-end root does not carry this run ownership marker.')
  }
  return { root: real, token }
}

/** Resolves `candidate` and requires it to be strictly inside the verified root. */
export function resolveInsideOwnedRoot(owned: OwnedRoot, candidate: string | undefined): string {
  if (!candidate || !isAbsolute(candidate)) {
    throw new Error('The path is missing or not absolute.')
  }
  const resolved = resolve(candidate)
  const relation = relative(owned.root, resolved)
  if (relation === '' || relation.startsWith('..') || isAbsolute(relation)) {
    throw new Error('The path is outside the owned end-to-end root.')
  }
  return resolved
}

/** Deletes the smoke database and its WAL and SHM files, only inside a verified owned root. */
export function removeOwnedDatabase(root: string | undefined, token: string | undefined, databasePath: string | undefined) {
  const owned = verifyOwnedRoot(root, token)
  const database = resolveInsideOwnedRoot(owned, databasePath)
  if (basename(database) !== DATABASE_FILE || dirname(database) !== owned.root) {
    throw new Error('The path is not the smoke database of the owned root.')
  }
  for (const suffix of ['', '-wal', '-shm']) {
    const path = `${database}${suffix}`
    const stat = lstatSync(path, { throwIfNoEntry: false })
    if (!stat) {
      continue
    }
    if (!stat.isFile() || stat.isSymbolicLink()) {
      throw new Error('A database file is not a regular file.')
    }
    unlinkSync(path)
  }
}

/** Removes the whole root, only after verifying ownership. */
export function removeOwnedRoot(root: string | undefined, token: string | undefined) {
  const owned = verifyOwnedRoot(root, token)
  if (existsSync(owned.root)) {
    rmSync(owned.root, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 })
  }
}

const CACHE = Symbol.for('devalcopilot.e2e.owned-root')

/**
 * The orchestrating process always creates a fresh root (an inherited path is ignored); a worker reuses only a root that
 * passes verification against the token the orchestrator exported, and fails closed otherwise.
 */
export function resolveRunRoot(env: NodeJS.ProcessEnv, isWorker: boolean): OwnedRoot {
  const cache = globalThis as unknown as Record<symbol, OwnedRoot | undefined>
  if (isWorker) {
    return verifyOwnedRoot(env[ROOT_ENV], env[TOKEN_ENV])
  }
  if (!cache[CACHE]) {
    cache[CACHE] = createOwnedRoot()
  }
  return cache[CACHE]
}
