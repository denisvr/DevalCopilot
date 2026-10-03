import type { InvocationEntry } from './journeyEnv.ts'

// The judge of "which plan did this stage see": pure, so a regression test can prove it detects a substitution of the Planner root or of
// an intermediate revision for the implemented Proposal, a missing stage, and a wrong authorization fact. Identities are compared
// case-insensitively because the host writes uppercase GUIDs to SQLite and the doubles log lowercase ones.

export interface PlanIdentities {
  /** The Planner's original Proposal. */
  rootId: string
  /** The plan the Implementer was asked to implement and every later plan-bearing stage must judge. */
  revisedId: string
  /** Earlier revisions that are neither the root nor the implemented plan (the first revision of an escalated lineage). */
  intermediateIds?: readonly string[]
  /** The content marker the implemented plan carries. */
  expectedMarker?: string
}

const same = (left: string | undefined, right: string) => left !== undefined && left.toLowerCase() === right.toLowerCase()

/** Contracts whose manifest carries the implemented plan: the implementation itself, the diagnosis, and the ordinary review. */
export const PLAN_BEARING_CONTRACTS = ['ImplementationReport', 'VerificationDiagnosis', 'ImplementationReview'] as const

/**
 * Returns the problems found in the invocation evidence; an empty list means every plan-bearing stage saw the implemented Proposal and
 * none saw the Planner root or an intermediate revision. At least one entry per plan-bearing contract is required, so a missing stage is
 * also a problem.
 */
export function planIdentityProblems(entries: readonly InvocationEntry[], plan: PlanIdentities): string[] {
  const problems: string[] = []
  const intermediates = plan.intermediateIds ?? []
  const expectedMarker = plan.expectedMarker ?? 'REVISED-PLAN'
  if (same(plan.rootId, plan.revisedId)) {
    problems.push('The revised Proposal and the Planner root are the same message, so the journey cannot discriminate them.')
  }
  for (const intermediate of intermediates) {
    if (same(intermediate, plan.revisedId) || same(intermediate, plan.rootId)) {
      problems.push('An intermediate revision is the same message as the implemented Proposal or the Planner root, so the journey cannot discriminate them.')
    }
  }
  for (const contract of PLAN_BEARING_CONTRACTS) {
    const seen = entries.filter((entry) => entry.contract === contract)
    if (seen.length === 0) {
      problems.push(`No ${contract} stage reached the doubles.`)
    }
    for (const entry of seen) {
      if (same(entry.planMessageId, plan.rootId)) {
        problems.push(`${contract} was given the Planner root instead of the revised Proposal.`)
      } else if (intermediates.some((intermediate) => same(entry.planMessageId, intermediate))) {
        problems.push(`${contract} was given an intermediate revision instead of the implemented Proposal.`)
      } else if (!same(entry.planMessageId, plan.revisedId)) {
        problems.push(`${contract} was given a plan that is neither the revised Proposal nor the Planner root.`)
      }
      if (entry.planMarker !== expectedMarker) {
        problems.push(`${contract} did not see the revised plan's content.`)
      }
    }
  }
  return problems
}

/** The identities of one escalated lineage and of the human authorization recorded for its final plan, as the host's own rows say. */
export interface EscalatedLineage {
  rootId: string
  firstRevisionId: string
  finalId: string
  /** The Challenge identities of each round, in collaboration order. */
  firstChallengeIds: readonly string[]
  secondChallengeIds: readonly string[]
  authorization: {
    authorizationId: string
    escalationMessageId: string
    instructionMessageId: string
    /** SHA-256 (lowercase hex) of the normalized rationale the person typed. */
    rationaleSha256: string
  }
}

/** The stage sequence the escalated journey must have caused, including the verification executable's two answers. */
export const ESCALATED_STAGE_SEQUENCE = [
  'Proposal',
  'CriticalReview',
  'ChallengeResolution',
  'CriticalReview',
  'ChallengeResolution',
  'ImplementationReport',
  'verify:failed',
  'VerificationDiagnosis',
  'ReviewCorrection',
  'verify:passed',
  'ImplementationReview',
] as const

/**
 * Judges the whole invocation evidence of an escalated journey against the host's own identities, independently of what the doubles
 * accepted: the stages happened in order and once each, both rounds saw their own proposal and challenges, the three plans are distinct,
 * the implementation, diagnosis and review all saw the final plan (never the root or the first revision), and the authorization facts
 * that reached the implementation are exactly the recorded ones and reached nothing else.
 */
export function escalatedLineageProblems(entries: readonly InvocationEntry[], lineage: EscalatedLineage): string[] {
  const problems: string[] = []
  const stages = entries.filter((entry) => entry.kind !== 'probe')
  const sequence = stages.map((entry) => entry.contract ?? `verify:${entry.outcome}`)
  if (sequence.join(',') !== ESCALATED_STAGE_SEQUENCE.join(',')) {
    problems.push(`The stage sequence was [${sequence.join(', ')}] instead of the escalated journey's.`)
  }
  if (new Set([lineage.rootId, lineage.firstRevisionId, lineage.finalId].map((id) => id.toLowerCase())).size !== 3) {
    problems.push('The root, the first revision and the final revision are not three distinct messages.')
  }

  problems.push(
    ...planIdentityProblems(entries, {
      rootId: lineage.rootId,
      revisedId: lineage.finalId,
      intermediateIds: [lineage.firstRevisionId],
      expectedMarker: 'FINAL-PLAN',
    }),
  )

  const reviews = stages.filter((entry) => entry.contract === 'CriticalReview')
  const resolutions = stages.filter((entry) => entry.contract === 'ChallengeResolution')
  const rounds = [
    { round: 'first', proposalId: lineage.rootId, marker: 'ROOT-PLAN', challenges: lineage.firstChallengeIds },
    { round: 'second', proposalId: lineage.firstRevisionId, marker: 'REVISED-PLAN', challenges: lineage.secondChallengeIds },
  ]
  rounds.forEach((expected, index) => {
    for (const [label, entry] of [['review', reviews[index]] as const, ['resolution', resolutions[index]] as const]) {
      if (!entry) {
        problems.push(`The ${expected.round} round has no ${label}.`)
        continue
      }
      if (!same(entry.planMessageId, expected.proposalId) || entry.planMarker !== expected.marker) {
        problems.push(`The ${expected.round} round ${label} did not work on its own proposal.`)
      }
      if (label === 'resolution' && entry.challengeCount !== expected.challenges.length) {
        problems.push(`The ${expected.round} round resolution did not receive exactly its own challenges.`)
      }
    }
  })

  const implementations = stages.filter((entry) => entry.contract === 'ImplementationReport')
  const expectedAuthorization = lineage.authorization
  for (const entry of implementations) {
    if (
      !same(entry.authorizationId, expectedAuthorization.authorizationId) ||
      !same(entry.escalationMessageId, expectedAuthorization.escalationMessageId) ||
      !same(entry.instructionMessageId, expectedAuthorization.instructionMessageId) ||
      entry.rationaleSha256 !== expectedAuthorization.rationaleSha256
    ) {
      problems.push('The implementation did not receive exactly the recorded human authorization.')
    }
    if (
      entry.decisionCount !== lineage.secondChallengeIds.length ||
      entry.decisionChallengeIds?.toLowerCase() !== lineage.secondChallengeIds.map((id) => id.toLowerCase()).join(',')
    ) {
      problems.push('The implementation did not receive exactly the ordered second-round decisions.')
    }
  }
  if (
    stages.some(
      (entry) =>
        entry.contract !== 'ImplementationReport' &&
        (entry.authorizationId !== undefined || entry.rationaleSha256 !== undefined || entry.decisionCount !== undefined),
    )
  ) {
    problems.push('An authorization fact reached a stage other than the implementation.')
  }
  return problems
}
