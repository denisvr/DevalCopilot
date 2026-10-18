import {
  AgentAttemptArtifactMetadataResponse,
  AgentAttemptEvidenceResponse,
  AgentAttemptHistoryEntryResponse,
  AgentAttemptHistoryResponse,
  SealedAgentArtifactWindowResponse,
} from '../../api/generated/api-client'
import type { IAgentAttemptEvidenceResponse, IAgentAttemptHistoryEntryResponse } from '../../api/generated/api-client'

/** Test-only builders of coherent Agent-attempt history, evidence and window responses; every field can be replaced to build a contradiction. */
export const attemptIdFor = (runId: string, attemptNumber: number) => `${runId}-attempt-${attemptNumber}`

export const attemptEntryFor = (runId: string, attemptNumber: number, patch: Partial<IAgentAttemptHistoryEntryResponse> = {}) =>
  new AgentAttemptHistoryEntryResponse({
    attemptId: attemptIdFor(runId, attemptNumber),
    attemptNumber,
    status: 'Failed',
    claimedAtUtc: new Date(Date.UTC(2026, 9, 9, 10, attemptNumber % 60, 0)),
    identityValid: true,
    role: 'Planner',
    provider: 'Codex',
    responseContract: 'Proposal',
    outcome: 'AccountUsageStopReached',
    ...patch,
  })

/** One page of the given attempt numbers (descending), with `hasMore` and the cursor derived exactly as the host derives them. */
export const attemptPageOf = (runId: string, attemptNumbers: readonly number[], hasMore = false) =>
  new AgentAttemptHistoryResponse({
    items: attemptNumbers.map((number) => attemptEntryFor(runId, number)),
    hasMore,
    nextBeforeAttemptNumber: hasMore ? attemptNumbers[attemptNumbers.length - 1] : undefined,
  })

/** Attempt numbers from `from` down to `to`, inclusive. */
export const descendingAttempts = (from: number, to: number) => Array.from({ length: from - to + 1 }, (_, index) => from - index)

export const attemptEvidenceFor = (
  runId: string,
  attemptNumber: number,
  purposes: readonly string[] = ['AgentContextManifest'],
  patch: Partial<IAgentAttemptEvidenceResponse> = {},
) =>
  new AgentAttemptEvidenceResponse({
    identityValid: true,
    attemptId: attemptIdFor(runId, attemptNumber),
    attemptNumber,
    attemptStatus: 'Failed',
    claimedAtUtc: new Date(Date.UTC(2026, 9, 9, 10, attemptNumber % 60, 0)),
    role: 'Planner',
    provider: 'Codex',
    responseContract: 'Proposal',
    outcome: 'AccountUsageStopReached',
    artifacts: purposes.map(
      (purpose) => new AgentAttemptArtifactMetadataResponse({ purpose, byteLength: 21, truncated: false, captureOutcome: 'Captured' }),
    ),
    ...patch,
  })

/** A verified window of `text` read from `fromOffset`, with the host's cursor fields (ASCII text, so characters equal bytes). */
export const windowOf = (text: string, fromOffset = 0, total = fromOffset + text.length) =>
  new SealedAgentArtifactWindowResponse({ status: 'Ok', text, nextOffset: fromOffset + text.length, totalLengthSoFar: total, truncated: false })
