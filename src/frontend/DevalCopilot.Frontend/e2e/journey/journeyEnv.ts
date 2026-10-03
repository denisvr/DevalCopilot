import { execFileSync } from 'node:child_process'
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { ROOT_ENV, TOKEN_ENV, verifyOwnedRoot } from '../harness/ownedRoot.ts'
import type { OwnedRoot } from '../harness/ownedRoot.ts'

// Everything the journey needs from its run, derived from the environment the journey configuration exported. It deliberately
// does not import playwright.config.ts (which would create a second root and secret), and it never writes the launch secret
// anywhere: the secret exists only in the process environment and in the page's init script.

export const JOURNEY_API_URL = 'http://127.0.0.1:5099'

export function journeySecret(): string {
  const secret = process.env.DEVALCOPILOT_TEST_LAUNCH_SECRET
  if (!secret) {
    throw new Error('The journey launch secret is not available.')
  }
  return secret
}

/** The verified owned root of this run; throws unless it is the directory this run created. */
export function journeyRoot(): OwnedRoot {
  return verifyOwnedRoot(process.env[ROOT_ENV], process.env[TOKEN_ENV])
}

export const CANDIDATE_RELATIVE_PATH = 'src/Feature.cs'
export const CANDIDATE_BASELINE = [
  'namespace Fixture;',
  '',
  'public static class Feature',
  '{',
  '    public static int Total(int left, int right)',
  '    {',
  '        return 0;',
  '    }',
  '}',
  '',
].join('\n')

export interface JourneyRepository {
  path: string
  baselineCommit: string
}

/** The disposable source repository (test setup, not workflow): one baseline commit holding the stub candidate file. */
export function createJourneyRepository(name: string): JourneyRepository {
  const path = join(journeyRoot().root, 'repos', name)
  mkdirSync(join(path, 'src'), { recursive: true })
  writeFileSync(join(path, 'README.md'), `# ${name}\n`)
  writeFileSync(join(path, CANDIDATE_RELATIVE_PATH), CANDIDATE_BASELINE)
  const git = (...args: string[]) => execFileSync('git', ['-C', path, ...args], { stdio: 'pipe' }).toString().trim()
  git('init', '--initial-branch', 'main')
  // No line-ending conversion anywhere for this repository: the host runs Git with a cleared environment (no global
  // configuration), so a machine-wide autocrlf would otherwise check the worktree out with CRLF while the host's Git compares it
  // to LF blobs and reports every file as changed. The repository-level setting is shared by the worktrees created from it.
  git('config', 'core.autocrlf', 'false')
  git('add', 'README.md', CANDIDATE_RELATIVE_PATH)
  git('-c', 'user.name=Fixture Author', '-c', 'user.email=fixture@example.invalid', 'commit', '-m', 'Initial commit')
  return { path, baselineCommit: git('rev-parse', 'HEAD') }
}

/** A snapshot of the source repository that a later read can compare: HEAD, the work tree status, and the candidate bytes. */
export function repositorySnapshot(path: string): { head: string; status: string; candidate: string; branches: string } {
  const git = (...args: string[]) => execFileSync('git', ['-C', path, ...args], { stdio: 'pipe' }).toString()
  return {
    head: git('rev-parse', 'HEAD').trim(),
    status: git('status', '--porcelain=v1', '--untracked-files=all'),
    candidate: readFileSync(join(path, CANDIDATE_RELATIVE_PATH), 'utf8'),
    branches: git('for-each-ref', '--format=%(refname) %(objectname)'),
  }
}

export interface InvocationEntry {
  role: string
  kind: string
  contract?: string
  planMessageId?: string
  planMarker?: string
  reportMessageId?: string
  findingCount?: number
  challengeCount?: number
  changedPath?: string
  outcome?: string
  guidanceSha256?: string
  guidanceBoundary?: string
}

/** The doubles' invocation log exactly as written, for a check that something never appears in it. */
export function readInvocationLogText(): string {
  const path = join(journeyRoot().root, 'fixture', 'invocations.jsonl')
  return existsSync(path) ? readFileSync(path, 'utf8') : ''
}

/** The doubles' allowlisted invocation evidence, in order (see the provider fixture's invocation log). */
export function readInvocations(): InvocationEntry[] {
  const path = join(journeyRoot().root, 'fixture', 'invocations.jsonl')
  if (!existsSync(path)) {
    return []
  }
  return readFileSync(path, 'utf8')
    .split('\n')
    .filter((line) => line.length > 0)
    .map((line) => JSON.parse(line) as InvocationEntry)
}

export function readLaunchTargetVerdict(): { verified: boolean; codexOwned: boolean; claudeOwned: boolean } | null {
  const path = join(journeyRoot().root, 'fixture', 'launch-targets.json')
  return existsSync(path) ? (JSON.parse(readFileSync(path, 'utf8')) as { verified: boolean; codexOwned: boolean; claudeOwned: boolean }) : null
}
