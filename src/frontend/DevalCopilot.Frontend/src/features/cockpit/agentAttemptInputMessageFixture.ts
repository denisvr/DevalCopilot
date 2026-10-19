import { attemptIdFor } from './agentAttemptHistoryFixture'
import { inputReferenceFor, inputsExpectationFor, inputsHeaderFor } from './agentAttemptInputsFixture'
import type { AgentAttemptInputReference } from './agentAttemptInputs'
import { MESSAGE_SCHEMAS } from './collaborationHistory'

/**
 * Test-only builders of the parsed selected-input answers the host writes (ADR-0053), consistent with the reference fixture; every field
 * can be replaced to build a contradiction.
 */
export const inputFieldValue = (name: string, position: number) => `Recorded ${name} text of input ${position}.`

export const inputMessageFor = (runId: string, attemptNumber: number, position: number, patch: Record<string, unknown> = {}) => {
  const reference = inputReferenceFor(runId, attemptNumber, position)
  return {
    runId,
    id: reference.collaborationMessageId,
    sequence: reference.collaborationSequence,
    occurredAtUtc: reference.occurredAtUtc,
    protocolVersion: '1.0',
    type: reference.type,
    provenance: 'ProviderObserved',
    actor: { kind: 'Agent', role: 'Planner', provider: 'Codex' },
    recipient: { kind: 'Orchestrator', role: null, provider: null },
    contentStatus: 'Available',
    summary: `Recorded summary of input ${position}.`,
    fields: MESSAGE_SCHEMAS[reference.type].map(([name]) => ({ name, value: inputFieldValue(name, position) })),
    ...patch,
  }
}

export const recordedInputMessageFor = (
  runId: string,
  attemptNumber: number,
  position: number,
  patch: Record<string, unknown> = {},
  messagePatch: Record<string, unknown> = {},
) => ({
  runId,
  attemptId: attemptIdFor(runId, attemptNumber),
  collaborationMessageId: inputReferenceFor(runId, attemptNumber, position).collaborationMessageId,
  status: 'Recorded',
  attempt: inputsHeaderFor(runId, attemptNumber),
  input: inputReferenceFor(runId, attemptNumber, position),
  message: inputMessageFor(runId, attemptNumber, position, messagePatch),
  ...patch,
})

export const unavailableInputMessageFor = (runId: string, attemptNumber: number, position: number, patch: Record<string, unknown> = {}) => ({
  runId,
  attemptId: attemptIdFor(runId, attemptNumber),
  collaborationMessageId: inputReferenceFor(runId, attemptNumber, position).collaborationMessageId,
  status: 'Unavailable',
  attempt: null,
  input: null,
  message: null,
  ...patch,
})

/** The decoded reference a selection carries, exactly as the reference decoder admits it. */
export const acceptedReferenceFor = (runId: string, attemptNumber: number, position: number): AgentAttemptInputReference => {
  const { sequence, collaborationMessageId, type, collaborationSequence, occurredAtUtc } = inputReferenceFor(runId, attemptNumber, position)
  return { position: sequence, collaborationMessageId, type, collaborationSequence, occurredAtUtc }
}

/** What a selected input's answer is checked against. */
export const inputMessageExpectationFor = (runId: string, attemptNumber: number, position: number) => ({
  ...inputsExpectationFor(runId, attemptNumber),
  reference: acceptedReferenceFor(runId, attemptNumber, position),
})
