import { attemptIdFor } from './agentAttemptHistoryFixture'
import { outputReferenceFor, outputsExpectationFor, outputsHeaderFor } from './agentAttemptOutputsFixture'
import type { AgentAttemptOutputReference } from './agentAttemptOutputs'
import { MESSAGE_SCHEMAS } from './collaborationHistory'
import type { KnownMessageType } from './collaborationHistory'

/**
 * Test-only builders of the parsed selected-output answers the host writes (ADR-0054), consistent with the reference fixture; every
 * field can be replaced to build a contradiction.
 */
export const outputFieldValue = (name: string, position: number) => `Recorded ${name} text of output ${position}.`

export const outputMessageFor = (runId: string, attemptNumber: number, position: number, patch: Record<string, unknown> = {}) => {
  const reference = outputReferenceFor(runId, attemptNumber, position)
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
    summary: `Recorded summary of output ${position}.`,
    fields: MESSAGE_SCHEMAS[reference.type as KnownMessageType].map(([name]) => ({ name, value: outputFieldValue(name, position) })),
    ...patch,
  }
}

export const recordedOutputMessageFor = (
  runId: string,
  attemptNumber: number,
  position: number,
  patch: Record<string, unknown> = {},
  messagePatch: Record<string, unknown> = {},
) => ({
  runId,
  attemptId: attemptIdFor(runId, attemptNumber),
  collaborationMessageId: outputReferenceFor(runId, attemptNumber, position).collaborationMessageId,
  status: 'Recorded',
  attempt: outputsHeaderFor(runId, attemptNumber),
  output: outputReferenceFor(runId, attemptNumber, position),
  message: outputMessageFor(runId, attemptNumber, position, messagePatch),
  ...patch,
})

export const unavailableOutputMessageFor = (runId: string, attemptNumber: number, position: number, patch: Record<string, unknown> = {}) => ({
  runId,
  attemptId: attemptIdFor(runId, attemptNumber),
  collaborationMessageId: outputReferenceFor(runId, attemptNumber, position).collaborationMessageId,
  status: 'Unavailable',
  attempt: null,
  output: null,
  message: null,
  ...patch,
})

/** The decoded reference a selection carries, exactly as the reference decoder admits it. */
export const acceptedOutputReferenceFor = (runId: string, attemptNumber: number, position: number): AgentAttemptOutputReference => {
  const { collaborationMessageId, type, collaborationSequence, occurredAtUtc } = outputReferenceFor(runId, attemptNumber, position)
  return { position, collaborationMessageId, type: type as AgentAttemptOutputReference['type'], collaborationSequence, occurredAtUtc }
}

/** What a selected output's answer is checked against. */
export const outputMessageExpectationFor = (runId: string, attemptNumber: number, position: number) => ({
  ...outputsExpectationFor(runId, attemptNumber),
  reference: acceptedOutputReferenceFor(runId, attemptNumber, position),
})
