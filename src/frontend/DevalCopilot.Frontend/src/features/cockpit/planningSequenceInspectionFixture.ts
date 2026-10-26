/** Test-only builders of the parsed recorded planning sequence inspection answers the host writes (ADR-0068); every field can be replaced to build a contradiction. */
export const FINGERPRINT = 'a'.repeat(64)

export const guidOf = (kind: string, n: number): string => {
  const hex = (kind.charCodeAt(0) * 1000 + n).toString(16).padStart(12, '0')
  return `00000000-0000-4000-8000-${hex}`
}

export const RUN = guidOf('r', 1)
export const SEQUENCE = guidOf('s', 1)
export const WORKSPACE = guidOf('w', 1)
export const CHECKPOINT = guidOf('c', 1)

const instant = (second: number) => `2026-10-20T09:00:${String(second).padStart(2, '0')}.0000000Z`
export const instantAt = instant

export const ROLES = ['Planner', 'CriticalReviewer', 'Resolver', 'CriticalReviewer', 'Resolver'] as const
export const PROVIDERS = ['Codex', 'ClaudeCode', 'Codex', 'ClaudeCode', 'Codex'] as const
export const CONTRACTS = ['Proposal', 'CriticalReview', 'ChallengeResolution', 'CriticalReview', 'ChallengeResolution'] as const

export const stepIdFor = (ordinal: number) => guidOf('t', ordinal)
export const attemptIdFor = (ordinal: number) => guidOf('a', ordinal)

/** The Proposal each step names: the reviewer of a round and its Resolver share one (rounds: steps 1 and 2, steps 3 and 4). */
export const proposalFor = (ordinal: number) => guidOf('p', ordinal <= 2 ? 1 : 2)

/** One recorded step member of the five-step progression, Concluded and linked by default. */
export const stepFor = (ordinal: number, patch: Record<string, unknown> = {}) => ({
  stepId: stepIdFor(ordinal),
  ordinal,
  role: ROLES[ordinal],
  provider: PROVIDERS[ordinal],
  responseContract: CONTRACTS[ordinal],
  recordedState: 'Concluded',
  reasonCode: null,
  revision: '3',
  predecessorAttemptId: ordinal === 0 ? null : attemptIdFor(ordinal - 1),
  inputProposalMessageId: ordinal === 0 ? null : proposalFor(ordinal),
  attemptId: attemptIdFor(ordinal),
  preparationReservedAtUtc: instant(ordinal * 10),
  preparationDeadlineUtc: instant(ordinal * 10 + 5),
  updatedAtUtc: instant(ordinal * 10 + 8),
  ...patch,
})

/** The recorded step of an outstanding (Reserved) tail: no link, no reason. */
export const reservedStepFor = (ordinal: number, patch: Record<string, unknown> = {}) =>
  stepFor(ordinal, { recordedState: 'Reserved', attemptId: null, revision: '1', updatedAtUtc: instant(ordinal * 10), ...patch })

export const headerFor = (patch: Record<string, unknown> = {}) => ({
  sequenceId: SEQUENCE,
  workspaceId: WORKSPACE,
  checkpointId: CHECKPOINT,
  fingerprintSha256: FINGERPRINT,
  recordedState: 'AwaitingHumanDecision',
  reasonCode: 'second_round_escalated',
  revision: '12',
  armedAtUtc: instant(0),
  updatedAtUtc: instant(50),
  stopRequestedAtUtc: null,
  endedAtUtc: instant(50),
  ...patch,
})

export const FIVE_STEPS = [0, 1, 2, 3, 4].map((ordinal) => stepFor(ordinal))

export const recordedIndexFor = (
  runId = RUN,
  steps: readonly Record<string, unknown>[] = FIVE_STEPS,
  patch: Record<string, unknown> = {},
) => ({
  runId,
  status: 'Recorded',
  sequence: headerFor(),
  steps,
  ...patch,
})

export const notRecordedIndexFor = (runId = RUN) => ({ runId, status: 'NotRecorded', sequence: null, steps: [] })
export const unavailableIndexFor = (runId = RUN) => ({ runId, status: 'Unavailable', sequence: null, steps: [] })

export const agentFor = (ordinal = 0, patch: Record<string, unknown> = {}) => ({
  id: attemptIdFor(ordinal),
  attemptNumber: ordinal + 1,
  role: ROLES[ordinal],
  provider: PROVIDERS[ordinal],
  responseContract: CONTRACTS[ordinal],
  ...patch,
})

export const stepAnswerFor = (ordinal = 0, patch: Record<string, unknown> = {}) => ({
  runId: RUN,
  sequenceId: SEQUENCE,
  stepId: stepIdFor(ordinal),
  status: 'Recorded',
  step: stepFor(ordinal),
  agent: agentFor(ordinal),
  ...patch,
})

export const refusedStepAnswerFor = (ordinal: number, status: 'NoLinkedAttempt' | 'Unavailable') => ({
  runId: RUN,
  sequenceId: SEQUENCE,
  stepId: stepIdFor(ordinal),
  status,
  step: null,
  agent: null,
})
