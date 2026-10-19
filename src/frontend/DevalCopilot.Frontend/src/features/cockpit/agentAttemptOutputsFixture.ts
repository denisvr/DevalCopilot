import { attemptIdFor } from './agentAttemptHistoryFixture'

/** Test-only builders of the parsed recorded-output answers the host writes (ADR-0054); every field can be replaced to build a contradiction. */
export const outputIdFor = (runId: string, attemptNumber: number, position: number) => `${runId}-attempt-${attemptNumber}-output-${position}`

// The history fixture's attempts are Planner attempts, which may author exactly these types.
const TYPES = ['Proposal', 'Question', 'Escalation'] as const

export const outputReferenceFor = (runId: string, attemptNumber: number, position: number, patch: Record<string, unknown> = {}) => ({
  position,
  collaborationMessageId: outputIdFor(runId, attemptNumber, position),
  // Widened so a test can patch in a type this fixture never writes.
  type: TYPES[position % TYPES.length] as string,
  collaborationSequence: String(100 + position),
  occurredAtUtc: `2026-10-09T10:${String(position % 60).padStart(2, '0')}:00.1234567Z`,
  ...patch,
})

/** The selected attempt the answer is checked against; the history fixture's attempts are Planner attempts of Codex. */
export const outputsHeaderFor = (runId: string, attemptNumber: number, patch: Record<string, unknown> = {}) => ({
  id: attemptIdFor(runId, attemptNumber),
  attemptNumber,
  role: 'Planner',
  provider: 'Codex',
  responseContract: 'Proposal',
  ...patch,
})

export const recordedOutputsFor = (runId: string, attemptNumber: number, count: number, patch: Record<string, unknown> = {}) => ({
  runId,
  attemptId: attemptIdFor(runId, attemptNumber),
  status: 'Recorded',
  attempt: outputsHeaderFor(runId, attemptNumber),
  outputMessageTotalCount: count,
  outputMessages: Array.from({ length: count }, (_, position) => outputReferenceFor(runId, attemptNumber, position)),
  ...patch,
})

export const emptyOutputsFor = (runId: string, attemptNumber: number, patch: Record<string, unknown> = {}) => ({
  runId,
  attemptId: attemptIdFor(runId, attemptNumber),
  status: 'Empty',
  attempt: outputsHeaderFor(runId, attemptNumber),
  outputMessageTotalCount: 0,
  outputMessages: [],
  ...patch,
})

export const unavailableOutputsFor = (runId: string, attemptNumber: number, patch: Record<string, unknown> = {}) => ({
  runId,
  attemptId: attemptIdFor(runId, attemptNumber),
  status: 'Unavailable',
  attempt: null,
  outputMessageTotalCount: null,
  outputMessages: [],
  ...patch,
})

/** What a history-fixture attempt's answer is checked against. */
export const outputsExpectationFor = (runId: string, attemptNumber: number) => ({
  runId,
  attemptId: attemptIdFor(runId, attemptNumber),
  attemptNumber,
  role: 'Planner' as const,
  provider: 'Codex' as const,
  responseContract: 'Proposal' as const,
})
