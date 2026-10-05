import { existsSync, mkdirSync, rmSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { journeyRoot } from './journeyEnv.ts'
import type { InvocationEntry } from './journeyEnv.ts'

// The browser journey's support for the run-scoped Codex account-usage stop (ADR-0025): the scripted answers the owned Codex double
// gives to the host's strict account-usage observation (written into the owned root only), the observation reads it logged, and a
// pure judge of what the host recorded and showed. The expectations are written here independently of the host's code, and nothing in
// this file ever writes outside the owned root, reads a credential, or needs a production value.

/** One scripted account-usage read of the double: used percentages (either window may be absent) or a protocol error. */
export interface ScriptedUsageRead {
  primary?: number
  secondary?: number
  reachedType?: string
  unavailable?: boolean
}

const fixtureDirectory = (): string => join(journeyRoot().root, 'fixture')

/**
 * Starts a scenario: writes the scripted reads (the read at index N answers the Nth observation of this scenario, the last one
 * repeats) and resets the double's read counter so the first observation is index 0.
 */
export function startAccountUsageScript(reads: readonly ScriptedUsageRead[]): void {
  mkdirSync(fixtureDirectory(), { recursive: true })
  rmSync(join(fixtureDirectory(), 'account-usage.reads'), { force: true })
  writeFileSync(join(fixtureDirectory(), 'account-usage.json'), JSON.stringify({ reads }), 'utf8')
}

/**
 * Replaces the scripted reads WITHOUT resetting the double's read counter: the `consumed` observations already made are padded with a
 * harmless low read, so the first scripted read of `reads` answers the next observation. Only valid while no observation is in flight.
 */
export function continueAccountUsageScript(consumed: number, reads: readonly ScriptedUsageRead[]): void {
  mkdirSync(fixtureDirectory(), { recursive: true })
  const padded = [...Array.from({ length: consumed }, (): ScriptedUsageRead => ({ primary: 1 })), ...reads]
  writeFileSync(join(fixtureDirectory(), 'account-usage.json'), JSON.stringify({ reads: padded }), 'utf8')
}

/** Removes the script, so a later observation by this root's double is refused (nothing in a later journey relies on it). */
export function endAccountUsageScript(): void {
  rmSync(join(fixtureDirectory(), 'account-usage.json'), { force: true })
  rmSync(join(fixtureDirectory(), 'account-usage.reads'), { force: true })
}

export function accountUsageScriptExists(): boolean {
  return existsSync(join(fixtureDirectory(), 'account-usage.json'))
}

/** The indices of the strict account-usage observations the host made, in order, from the double's allowlisted invocation log. */
export function observationIndices(entries: readonly InvocationEntry[]): number[] {
  return entries.filter((entry) => entry.kind === 'account_usage').map((entry) => entry.usageReadIndex ?? -1)
}

/** The Agent stage invocations of the double (the capability probes and the account-usage observations are not stages). */
export function agentStageInvocations(entries: readonly InvocationEntry[]): InvocationEntry[] {
  return entries.filter((entry) => entry.kind !== 'probe' && entry.kind !== 'account_usage')
}

/** What the evidence response (as the generated client received it) carries for an attempt the stop refused before dispatch. */
export interface ExpectedStoppedAttempt {
  attemptNumber: number
  /** The claimed threshold. */
  threshold: number
  /** The windows the decision must have used, ordered by bucket then primary before secondary. */
  windows: readonly { bucketId: string | null; window: 'Primary' | 'Secondary'; usedPercent: number }[]
}

export interface ObservedStoppedAttempt {
  status: string
  outcome: string | null
  dispatchedAtUtc: string | null
  /** Raw database facts. */
  storedThreshold: number | null
  storedDecision: string | null
  /** The evidence response member by member. */
  stop: { state: string; percent: number | null } | null
  decision: {
    state: string
    decision: string | null
    reason: string | null
    thresholdPercent: number | null
    retrievedAtUtc: string | null
    windows: { bucketId: string | null; window: string; usedPercent: number }[]
  } | null
  /** The selected-attempt evidence the page rendered. */
  renderedText: string
}

/** The one canonical text the host persists for a reached decision, from the facts the double was scripted with. */
export function canonicalReachedDecision(threshold: number, retrievedAtUtc: string, windows: ExpectedStoppedAttempt['windows']): string {
  // The host writes a fixed seven-fraction-digit UTC form; the response carries the same instant with trailing zeros trimmed.
  const parts = /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(?:Z|\+00:00)$/.exec(retrievedAtUtc)
  const text = parts ? `${parts[1]}.${(parts[2] ?? '').padEnd(7, '0')}Z` : retrievedAtUtc
  return JSON.stringify({
    version: 1,
    source: 'codex-account-rate-limits-v1',
    decision: 'reached',
    reason: 'threshold_reached',
    thresholdPercent: threshold,
    retrievedAtUtc: text,
    windows: windows.map((window) => ({ bucket: window.bucketId, window: window.window === 'Primary' ? 'primary' : 'secondary', usedPercent: window.usedPercent })),
  })
}

const FORBIDDEN_CLAIMS = [/eligible/i, /safe to (invoke|start|run)/i, /remaining (quota|capacity|usage)/i, /capacity available/i, /live capacity/i]

/**
 * The problems of one attempt stopped before dispatch because a scripted window reached the claimed threshold: never dispatched,
 * failed with the one outcome, the threshold snapshot and the canonical bounded decision persisted, the evidence response and the
 * rendered page agreeing with them, and no provider text, account identity or unproven claim shown. An empty list is success.
 */
export function stoppedAttemptProblems(expected: ExpectedStoppedAttempt, observed: ObservedStoppedAttempt): string[] {
  const problems: string[] = []
  const want = (condition: boolean, message: string) => {
    if (!condition) {
      problems.push(message)
    }
  }

  want(observed.status === 'Failed', `attempt ${expected.attemptNumber} is ${observed.status}, not Failed`)
  want(observed.outcome === 'AccountUsageStopReached', `attempt ${expected.attemptNumber} outcome is ${observed.outcome}`)
  want(observed.dispatchedAtUtc === null, `attempt ${expected.attemptNumber} has a dispatch marker`)
  want(observed.storedThreshold === expected.threshold, `the threshold snapshot is ${observed.storedThreshold}`)
  want(observed.stop?.state === 'Configured' && observed.stop.percent === expected.threshold, 'the evidence does not show the claimed threshold')

  const decision = observed.decision
  want(decision?.state === 'Recorded', 'the evidence carries no recorded decision')
  want(decision?.decision === 'Reached' && decision.reason === 'ThresholdReached', 'the decision is not Reached for the threshold')
  want(decision?.thresholdPercent === expected.threshold, 'the decision carries another threshold')
  want(
    JSON.stringify(decision?.windows ?? null) === JSON.stringify(expected.windows),
    `the decision windows are ${JSON.stringify(decision?.windows)}`,
  )
  if (decision?.retrievedAtUtc) {
    want(
      observed.storedDecision === canonicalReachedDecision(expected.threshold, decision.retrievedAtUtc, expected.windows),
      'the stored decision is not the one canonical text',
    )
  } else {
    problems.push('the decision has no retrieval instant')
  }

  const rendered = observed.renderedText
  want(
    rendered.includes(`Not started: a reported usage window reached the configured stop (${expected.threshold}%).`),
    'the page does not state the stop',
  )
  want(rendered.includes('Host retrieval time:'), 'the page does not show the host retrieval time')
  for (const window of expected.windows) {
    want(
      rendered.includes(`${window.bucketId ?? 'account'} ${window.window.toLowerCase()} window: ${window.usedPercent}% used`),
      `the page does not show the ${window.window} window`,
    )
  }
  // The page's own disclaimer ("... says nothing about ... remaining quota or live capacity") names what it does not claim.
  const claimed = rendered
    .split('\n')
    .filter((line) => !/says nothing about/i.test(line))
    .join('\n')
  for (const pattern of FORBIDDEN_CLAIMS) {
    want(!pattern.test(claimed), `the page makes an unproven claim (${pattern})`)
  }
  want(!/acct_|@|rate_limit|plan ?type|credits/i.test(rendered), 'the page shows provider or account text')
  return problems
}

/** What one explicit warning check (ADR-0026) must report for the double's scripted read. */
export interface ExpectedWarningCheck {
  threshold: number
  state: 'Below' | 'Reached'
  reason: 'ThresholdReached' | null
  windows: readonly { bucketId: string | null; window: 'Primary' | 'Secondary'; usedPercent: number; reachedThreshold: boolean }[]
}

export interface ObservedWarningCheck {
  status: number
  /** The response body exactly as the generated client received it. */
  body: {
    state: string
    reason: string | null
    thresholdPercent: number | null
    observedAtUtc: string | null
    windows: { bucketId: string | null; window: string; usedPercent: number; reachedThreshold: boolean }[]
    providerReportedLimitReached: boolean
  } | null
  /** The check result the page rendered (the status region only). */
  renderedText: string
  /** How many strict observations of the double this one check caused. */
  observationsAdded: number
  /** How many attempts this check created. */
  attemptsAdded: number
}

/**
 * The problems of one explicit warning check: exactly one observation and no attempt, the response and the rendered page both stating
 * the scripted outcome for the saved threshold with a host retrieval time, and no provider text, account identity, eligibility or
 * capacity claim. An empty list is success.
 */
export function warningCheckProblems(expected: ExpectedWarningCheck, observed: ObservedWarningCheck): string[] {
  const problems: string[] = []
  const want = (condition: boolean, message: string) => {
    if (!condition) {
      problems.push(message)
    }
  }

  want(observed.status === 200, `the check answered HTTP ${observed.status}`)
  want(observed.observationsAdded === 1, `the check caused ${observed.observationsAdded} observations, not one`)
  want(observed.attemptsAdded === 0, `the check created ${observed.attemptsAdded} attempts`)
  const body = observed.body
  want(body?.state === expected.state, `the check state is ${body?.state}`)
  want((body?.reason ?? null) === expected.reason, `the check reason is ${body?.reason}`)
  want(body?.thresholdPercent === expected.threshold, `the check concerns ${body?.thresholdPercent}%`)
  want(typeof body?.observedAtUtc === 'string' && !Number.isNaN(Date.parse(body.observedAtUtc)), 'the check has no host retrieval instant')
  want(body?.providerReportedLimitReached === false, 'the check reports a provider limit state')
  want(
    JSON.stringify(body?.windows ?? null) === JSON.stringify(expected.windows),
    `the check windows are ${JSON.stringify(body?.windows)}`,
  )

  const rendered = observed.renderedText
  const headline =
    expected.state === 'Below'
      ? `No reported usage window had reached the ${expected.threshold}% warning when the Codex account was observed.`
      : `A reported usage window had reached the ${expected.threshold}% warning when the Codex account was observed.`
  want(rendered.includes(headline), 'the page does not state the outcome')
  want(rendered.includes('Host retrieval time:'), 'the page does not show the host retrieval time')
  for (const window of expected.windows) {
    const line = `${window.bucketId ?? 'account'} ${window.window.toLowerCase()} window: ${window.usedPercent}% used${window.reachedThreshold ? ' (warning reached)' : ''}`
    want(rendered.includes(line), `the page does not show "${line}"`)
  }
  for (const pattern of FORBIDDEN_CLAIMS) {
    want(!pattern.test(rendered), `the page makes an unproven claim (${pattern})`)
  }
  want(!/acct_|@|rate_limit|plan ?type|credits/i.test(rendered), 'the page shows provider or account text')
  return problems
}
