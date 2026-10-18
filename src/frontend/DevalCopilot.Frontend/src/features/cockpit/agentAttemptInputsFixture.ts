import { attemptIdFor } from './agentAttemptHistoryFixture'

/** Test-only builders of the parsed recorded-input answers the host writes (ADR-0051); every field can be replaced to build a contradiction. */
export const inputIdFor = (runId: string, attemptNumber: number, position: number) => `${runId}-attempt-${attemptNumber}-input-${position}`

const TYPES = ['Proposal', 'Challenge', 'ExecutionReport', 'ReviewFinding', 'Decision'] as const

export const inputReferenceFor = (runId: string, attemptNumber: number, position: number, patch: Record<string, unknown> = {}) => ({
  sequence: position,
  collaborationMessageId: inputIdFor(runId, attemptNumber, position),
  type: TYPES[position % TYPES.length],
  collaborationSequence: String(100 + position),
  occurredAtUtc: `2026-10-09T10:${String(position % 60).padStart(2, '0')}:00.1234567Z`,
  ...patch,
})

/** The selected attempt the answer is checked against; the history fixture's attempts are Planner attempts of Codex. */
export const inputsHeaderFor = (runId: string, attemptNumber: number, patch: Record<string, unknown> = {}) => ({
  id: attemptIdFor(runId, attemptNumber),
  attemptNumber,
  role: 'Planner',
  provider: 'Codex',
  responseContract: 'Proposal',
  ...patch,
})

export const recordedInputsFor = (runId: string, attemptNumber: number, count: number, patch: Record<string, unknown> = {}) => ({
  runId,
  attemptId: attemptIdFor(runId, attemptNumber),
  status: 'Recorded',
  attempt: inputsHeaderFor(runId, attemptNumber),
  inputMessages: Array.from({ length: Math.min(count, 10) }, (_, position) => inputReferenceFor(runId, attemptNumber, position)),
  inputMessageTotalCount: count,
  inputMessagesOmitted: count > 10,
  ...patch,
})

export const emptyInputsFor = (runId: string, attemptNumber: number, patch: Record<string, unknown> = {}) => ({
  runId,
  attemptId: attemptIdFor(runId, attemptNumber),
  status: 'Empty',
  attempt: inputsHeaderFor(runId, attemptNumber),
  inputMessages: [],
  inputMessageTotalCount: 0,
  inputMessagesOmitted: false,
  ...patch,
})

export const unavailableInputsFor = (runId: string, attemptNumber: number, patch: Record<string, unknown> = {}) => ({
  runId,
  attemptId: attemptIdFor(runId, attemptNumber),
  status: 'Unavailable',
  attempt: null,
  inputMessages: [],
  inputMessageTotalCount: null,
  inputMessagesOmitted: false,
  ...patch,
})

/** What a history-fixture attempt's answer is checked against. */
export const inputsExpectationFor = (runId: string, attemptNumber: number) => ({
  runId,
  attemptId: attemptIdFor(runId, attemptNumber),
  attemptNumber,
  role: 'Planner' as const,
  provider: 'Codex' as const,
  responseContract: 'Proposal' as const,
})
