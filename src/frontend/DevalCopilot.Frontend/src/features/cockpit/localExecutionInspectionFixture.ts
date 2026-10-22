import type { LocalExecutionInspectionStep } from './localExecutionInspection'

/** Test-only builders of the parsed recorded local execution inspection answers the host writes (ADR-0061); every field can be replaced to build a contradiction. */
export const FINGERPRINT = 'a'.repeat(64)

export const guidOf = (kind: string, n: number): string => {
  const hex = (kind.charCodeAt(0) * 1000 + n).toString(16).padStart(12, '0')
  return `00000000-0000-4000-8000-${hex}`
}

export const RUN = guidOf('r', 1)
export const SEQUENCE = guidOf('s', 1)
export const CHECKPOINT = guidOf('c', 1)
export const PLAN = guidOf('p', 1)
export const REPORT = guidOf('m', 1)

const instant = (second: number) => `2026-10-20T09:00:${String(second).padStart(2, '0')}.0000000Z`

export const stepIdFor = (ordinal: number) => guidOf('t', ordinal)
export const operationIdFor = (ordinal: number) => guidOf('o', ordinal)

/** One recorded step member. The default cycle: implementation, a verification of recipe 0 and a code review, all concluded. */
export const stepFor = (ordinal: number, patch: Record<string, unknown> = {}) => {
  const kinds = ['Implementation', 'Verification', 'CodeReview']
  const kind = kinds[ordinal] ?? 'CodeReview'
  const verification = kind === 'Verification'
  return {
    stepId: stepIdFor(ordinal),
    ordinal,
    kind,
    passNumber: ordinal === 0 ? 0 : 1,
    recipeOrdinal: verification ? 0 : null,
    recipeName: verification ? 'Unit <b>tests</b>' : null,
    recordedState: 'Concluded',
    reasonCode: null,
    sourceCheckpointId: CHECKPOINT,
    sourceFingerprintSha256: FINGERPRINT,
    reportMessageId: ordinal === 0 ? null : REPORT,
    subjectAttemptId: null,
    operationId: operationIdFor(ordinal),
    preparationReservedAtUtc: instant(ordinal * 10),
    preparationDeadlineUtc: instant(ordinal * 10 + 1),
    claimedAtUtc: instant(ordinal * 10 + 2),
    resultDeadlineUtc: instant(ordinal * 10 + 9),
    ...patch,
  }
}

export const headerFor = (patch: Record<string, unknown> = {}) => ({
  sequenceId: SEQUENCE,
  planProposalMessageId: PLAN,
  planAuthority: 'AcceptedRoot',
  workspaceId: guidOf('w', 1),
  checkpointId: CHECKPOINT,
  fingerprintSha256: FINGERPRINT,
  recordedState: 'ReadyForHumanApproval',
  reasonCode: 'review_approved',
  revision: '9',
  recipeCount: 1,
  correctionAllowance: 2,
  armedAtUtc: instant(0),
  updatedAtUtc: instant(50),
  endedAtUtc: instant(50),
  ...patch,
})

export const recordedIndexFor = (runId = RUN, steps: readonly Record<string, unknown>[] = [stepFor(0), stepFor(1), stepFor(2)], patch: Record<string, unknown> = {}) => ({
  runId,
  status: 'Recorded',
  sequence: headerFor(),
  steps,
  ...patch,
})

export const notRecordedIndexFor = (runId = RUN) => ({ runId, status: 'NotRecorded', sequence: null, steps: [] })
export const unavailableIndexFor = (runId = RUN) => ({ runId, status: 'Unavailable', sequence: null, steps: [] })

export const streamFor = (id: string, byteLength: number, truncated: boolean | null = false) => ({
  status: 'Recorded',
  artifactId: id,
  byteLength,
  captureOutcome: truncated === null ? 'RecoveredAfterHostInterruption' : 'CapturedWithKnownTruncation',
  truncated,
})

export const absentStream = (status: 'NotRecorded' | 'Unavailable' = 'NotRecorded') => ({
  status,
  artifactId: null,
  byteLength: null,
  captureOutcome: null,
  truncated: null,
})

export const verificationFor = (ordinal = 1, patch: Record<string, unknown> = {}) => ({
  executionId: operationIdFor(ordinal),
  executionNumber: 4,
  commandName: 'Unit <b>tests</b>',
  status: 'Failed',
  outcome: 'Exited',
  exitCode: 1,
  claimedAtUtc: instant(12),
  dispatchedAtUtc: instant(13),
  completedAtUtc: instant(14),
  standardOutput: streamFor(guidOf('a', 1), 120),
  standardError: streamFor(guidOf('a', 2), 40),
  ...patch,
})

export const agentFor = (ordinal = 0, patch: Record<string, unknown> = {}) => ({
  id: operationIdFor(ordinal),
  attemptNumber: 3,
  role: 'Implementer',
  provider: 'ClaudeCode',
  responseContract: 'ImplementationReport',
  ...patch,
})

export const agentStepAnswerFor = (ordinal = 0, patch: Record<string, unknown> = {}) => ({
  runId: RUN,
  sequenceId: SEQUENCE,
  stepId: stepIdFor(ordinal),
  status: 'Recorded',
  step: stepFor(ordinal),
  agent: agentFor(ordinal),
  verification: null,
  ...patch,
})

export const verificationStepAnswerFor = (ordinal = 1, patch: Record<string, unknown> = {}) => ({
  runId: RUN,
  sequenceId: SEQUENCE,
  stepId: stepIdFor(ordinal),
  status: 'Recorded',
  step: stepFor(ordinal),
  agent: null,
  verification: verificationFor(ordinal),
  ...patch,
})

export const refusedStepAnswerFor = (ordinal: number, status: 'NoOperation' | 'Unavailable') => ({
  runId: RUN,
  sequenceId: SEQUENCE,
  stepId: stepIdFor(ordinal),
  status,
  step: null,
  agent: null,
  verification: null,
})

export const windowAnswerFor = (
  ordinal: number,
  stream: 'stdout' | 'stderr',
  artifact: ReturnType<typeof streamFor>,
  text: string,
  fromOffset: number,
  patch: Record<string, unknown> = {},
) => ({
  runId: RUN,
  sequenceId: SEQUENCE,
  stepId: stepIdFor(ordinal),
  executionId: operationIdFor(ordinal),
  stream,
  status: 'Ok',
  artifact,
  text,
  nextOffset: fromOffset + new TextEncoder().encode(text).length,
  totalLength: artifact.byteLength,
  ...patch,
})

export const refusedWindowFor = (
  ordinal: number,
  stream: 'stdout' | 'stderr',
  status: 'Unavailable' | 'NotRecorded' | 'Missing' | 'IntegrityMismatch',
  fromOffset: number,
  executionId: string | null = operationIdFor(ordinal),
) => ({
  runId: RUN,
  sequenceId: SEQUENCE,
  stepId: stepIdFor(ordinal),
  executionId,
  stream,
  status,
  artifact: null,
  text: '',
  nextOffset: fromOffset,
  totalLength: 0,
})

export const indexedStep = (ordinal: number, patch: Record<string, unknown> = {}) => stepFor(ordinal, patch) as unknown as LocalExecutionInspectionStep
