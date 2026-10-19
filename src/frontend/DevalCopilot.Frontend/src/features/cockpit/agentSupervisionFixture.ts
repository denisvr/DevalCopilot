import { toAgentSupervisionReading } from './agentSupervision'
import type { AgentSupervisionReading } from './agentSupervision'
import type { UseAgentSupervisionResult } from './hooks/useAgentSupervision'

// Test fixtures for the Agent supervision read (ADR-0056): complete host answers exactly as the wire carries them, each coherent for the
// status it builds. A test overrides one member to prove that the decoder refuses the incoherent form.

const ATTEMPT_ID = '11111111-1111-4111-8111-111111111111'
const HOST_ID = '22222222-2222-4222-8222-222222222222'
const OBSERVER_ID = '33333333-3333-4333-8333-333333333333'

/** The fixed host clock of the fixture reads: 09:01:00 UTC. */
export const OBSERVED_AT = '2026-10-19T09:01:00.0000000Z'

export type WireBody = Record<string, unknown>

export function attemptWire(overrides: WireBody = {}): WireBody {
  return {
    attemptId: ATTEMPT_ID,
    attemptNumber: 1,
    role: 'Planner',
    provider: 'Codex',
    responseContract: 'Proposal',
    status: 'Running',
    outcome: null,
    dispatched: false,
    claimedAtUtc: '2026-10-19T09:00:00.0000000Z',
    dispatchedAtUtc: null,
    completedAtUtc: null,
    ...overrides,
  }
}

export function observationWire(overrides: WireBody = {}): WireBody {
  return {
    hostInstanceId: HOST_ID,
    observerId: OBSERVER_ID,
    ownedByCurrentHost: true,
    revision: '3',
    phase: 'Preparing',
    startedAtUtc: '2026-10-19T09:00:00.0000000Z',
    lastHeartbeatAtUtc: '2026-10-19T09:00:50.0000000Z',
    phaseEnteredAtUtc: '2026-10-19T09:00:00.0000000Z',
    phaseDeadlineUtc: '2026-10-19T09:02:00.0000000Z',
    open: true,
    closureReason: null,
    closedAtUtc: null,
    ...overrides,
  }
}

export function attentionWire(overrides: WireBody = {}): WireBody {
  return {
    effectiveReasonCode: 'stale_heartbeat',
    effectiveReasonText: 'The host observer has not reported for at least thirty seconds. This does not show whether earlier work stopped.',
    effectiveAtUtc: '2026-10-19T09:00:30.0000000Z',
    recordedReasonCode: null,
    recordedAtUtc: null,
    ...overrides,
  }
}

function top(runId: string, overrides: WireBody): WireBody {
  return {
    runId,
    status: 'Observed',
    reasonCode: null,
    reasonText: null,
    nextActionCode: 'wait_and_refresh',
    nextActionText: 'Wait; the host observer is reporting.',
    attempt: attemptWire(),
    observation: observationWire(),
    attention: null,
    observedAtUtc: OBSERVED_AT,
    ...overrides,
  }
}

export const noAgentAttemptWire = (runId: string): WireBody =>
  top(runId, {
    status: 'NoAgentAttempt',
    nextActionCode: 'no_agent_attempt_recorded',
    nextActionText: 'No Agent attempt is recorded for this run.',
    attempt: null,
    observation: null,
  })

export const notObservedWire = (runId: string, terminal = false): WireBody =>
  top(runId, {
    status: 'NotObserved',
    nextActionCode: terminal ? 'review_the_recorded_result' : 'inspect_the_recorded_attempt',
    nextActionText: terminal ? 'Review the recorded result of the attempt.' : 'Inspect the recorded attempt; no observation is recorded for it.',
    attempt: terminal
      ? attemptWire({ status: 'Failed', outcome: 'ProviderInvocationFailed', completedAtUtc: '2026-10-19T09:00:30.0000000Z' })
      : attemptWire(),
    observation: null,
  })

export const observedWire = (runId: string, overrides: WireBody = {}): WireBody => top(runId, overrides)

export const needsAttentionWire = (runId: string, overrides: WireBody = {}): WireBody =>
  top(runId, {
    status: 'NeedsAttention',
    reasonCode: 'stale_heartbeat',
    reasonText: 'The host observer has not reported for at least thirty seconds. This does not show whether earlier work stopped.',
    nextActionCode: 'inspect_and_reconcile_before_further_work',
    nextActionText: 'Inspect the recorded attempt and reconcile it before any further work.',
    observation: observationWire({ lastHeartbeatAtUtc: '2026-10-19T09:00:00.0000000Z' }),
    attention: attentionWire(),
    ...overrides,
  })

export const endedWire = (runId: string, overrides: WireBody = {}): WireBody =>
  top(runId, {
    status: 'Ended',
    nextActionCode: 'review_the_recorded_result',
    nextActionText: 'Review the recorded result of the attempt.',
    attempt: attemptWire({
      status: 'Completed',
      outcome: 'Proposed',
      dispatched: true,
      dispatchedAtUtc: '2026-10-19T09:00:05.0000000Z',
      completedAtUtc: '2026-10-19T09:00:40.0000000Z',
    }),
    observation: observationWire({
      phase: 'RecordingResult',
      open: false,
      closureReason: 'attempt_terminal',
      closedAtUtc: '2026-10-19T09:00:41.0000000Z',
      lastHeartbeatAtUtc: '2026-10-19T09:00:40.0000000Z',
      phaseEnteredAtUtc: '2026-10-19T09:00:30.0000000Z',
      phaseDeadlineUtc: '2026-10-19T09:02:30.0000000Z',
    }),
    ...overrides,
  })

export const unavailableWire = (runId: string, reasonCode = 'chronology_invalid'): WireBody =>
  top(runId, {
    status: 'Unavailable',
    reasonCode,
    reasonText: 'The recorded observation times are not coherent with the host clock, so nothing is shown as fresh.',
    nextActionCode: 'inspect_and_reconcile_before_further_work',
    nextActionText: 'Inspect the recorded attempt and reconcile it before any further work.',
  })

/** The decoded reading of a fixture body for exactly `runId`; throws when the fixture itself is incoherent. */
export function readingOf(body: WireBody, runId: string): AgentSupervisionReading {
  const reading = toAgentSupervisionReading(body, runId)
  if (reading === null) {
    throw new Error('The supervision fixture is not a coherent answer.')
  }

  return reading
}

/** What `useAgentSupervision` returns for a validated reading received just now. */
export function resultFor(reading: AgentSupervisionReading | null, overrides: Partial<UseAgentSupervisionResult> = {}): UseAgentSupervisionResult {
  return {
    reading,
    loading: false,
    failure: null,
    receivedAtMilliseconds: reading === null ? null : 0,
    polling: reading !== null,
    refresh: () => undefined,
    ...overrides,
  }
}
