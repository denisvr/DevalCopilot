import { execFileSync } from 'node:child_process'
import { createHash } from 'node:crypto'
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

/** The two root instruction files every journey repository tracks, in the fixed order the host reads and reports them. */
export const INSTRUCTION_FILE_NAMES = ['AGENTS.md', 'CLAUDE.md'] as const

export interface InstructionFile {
  fileName: (typeof INSTRUCTION_FILE_NAMES)[number]
  /** The exact characters written to the repository (the file is UTF-8 with these line endings and no byte order mark). */
  text: string
}

/**
 * The root conventions of the repository called `name`, written independently of the host: distinct per repository so a stage that
 * received another project's files is detectable, with a multibyte character, quotes, angle brackets and CRLF endings so exact
 * preservation and escaping are exercised. The marker in each file never appears anywhere else in the product.
 */
export function instructionFiles(name: string): InstructionFile[] {
  return [
    {
      fileName: 'AGENTS.md',
      text: `# ${name} conventions\n\nKeep every change inside the plan-scoped file.\nQuote "names", prefer <Operation>Handler & friends — naïve 𝄞.\nCONVENTIONS-OF-${name}\n`,
    },
    { fileName: 'CLAUDE.md', text: `Claude notes for ${name}.\r\nSecond line, CRLF endings, trailing newline.\r\n` },
  ]
}

/** The SHA-256 of the UTF-8 bytes of a text, lower-case hexadecimal (the identity the host's section must carry for a Complete file). */
export const sha256OfText = (text: string): string => createHash('sha256').update(Buffer.from(text, 'utf8')).digest('hex')

/** The disposable source repository (test setup, not workflow): one baseline commit holding the stub candidate file and the two root instruction files. */
export function createJourneyRepository(name: string): JourneyRepository {
  const path = join(journeyRoot().root, 'repos', name)
  mkdirSync(join(path, 'src'), { recursive: true })
  writeFileSync(join(path, 'README.md'), `# ${name}\n`)
  writeFileSync(join(path, CANDIDATE_RELATIVE_PATH), CANDIDATE_BASELINE)
  for (const file of instructionFiles(name)) {
    writeFileSync(join(path, file.fileName), Buffer.from(file.text, 'utf8'))
  }
  const git = (...args: string[]) => execFileSync('git', ['-C', path, ...args], { stdio: 'pipe' }).toString().trim()
  git('init', '--initial-branch', 'main')
  // No line-ending conversion anywhere for this repository: the host runs Git with a cleared environment (no global
  // configuration), so a machine-wide autocrlf would otherwise check the worktree out with CRLF while the host's Git compares it
  // to LF blobs and reports every file as changed. The repository-level setting is shared by the worktrees created from it.
  git('config', 'core.autocrlf', 'false')
  git('add', 'README.md', CANDIDATE_RELATIVE_PATH, ...INSTRUCTION_FILE_NAMES)
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
  authorizationId?: string
  escalationMessageId?: string
  instructionMessageId?: string
  rationaleSha256?: string
  decisionCount?: number
  decisionChallengeIds?: string
  // What the double independently observed about the sealed project instruction context (never its text).
  instructionSection?: string
  instructionOrder?: string
  instructionBoundary?: string
  instructionBinding?: string
  instructionFiles?: string
  instructionStatuses?: string
  instructionByteLengths?: string
  instructionSha256?: string
  instructionTextsVerified?: string
  instructionReferences?: string
}

function invocationLines(): string[] {
  const path = join(journeyRoot().root, 'fixture', 'invocations.jsonl')
  return existsSync(path)
    ? readFileSync(path, 'utf8')
        .split('\n')
        .filter((line) => line.length > 0)
    : []
}

/**
 * The start of one journey's own interval of the doubles' append-only invocation log: the number of entries already written when the
 * journey began (another journey that ran earlier, in this run or an earlier test of it, is before the mark and never read). The
 * journeys of a run execute one after the other on the same host, so everything after the mark is this journey's.
 */
export type InvocationMark = number

export function markInvocations(): InvocationMark {
  return invocationLines().length
}

/** The invocation log text of the interval after the mark, exactly as written, for a check that something never appears in it. */
export function readInvocationLogText(since: InvocationMark): string {
  return invocationLines().slice(since).join('\n')
}

/** The doubles' allowlisted invocation evidence of the interval after the mark, in order (see the provider fixture's invocation log). */
export function readInvocations(since: InvocationMark): InvocationEntry[] {
  return invocationLines()
    .slice(since)
    .map((line) => JSON.parse(line) as InvocationEntry)
}

/** A sealed context manifest read back, read-only, from the owned artifact root (the stored relative path is the host's own). */
export function readSealedManifestText(relativeStoragePath: string): string {
  return readFileSync(join(journeyRoot().root, 'artifacts', relativeStoragePath), 'utf8')
}

export function readLaunchTargetVerdict(): { verified: boolean; codexOwned: boolean; claudeOwned: boolean } | null {
  const path = join(journeyRoot().root, 'fixture', 'launch-targets.json')
  return existsSync(path) ? (JSON.parse(readFileSync(path, 'utf8')) as { verified: boolean; codexOwned: boolean; claudeOwned: boolean }) : null
}
