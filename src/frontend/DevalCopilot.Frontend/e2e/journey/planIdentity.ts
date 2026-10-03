import type { InvocationEntry } from './journeyEnv.ts'

// The judge of "which plan did this stage see": pure, so a regression test can prove it detects a substitution of the Planner root
// for the revised (implemented) Proposal. Identities are compared case-insensitively because the host writes uppercase GUIDs to
// SQLite and the doubles log lowercase ones.

export interface PlanIdentities {
  /** The Planner's original Proposal. */
  rootId: string
  /** The Resolver's revised Proposal, the one the Implementer was asked to implement. */
  revisedId: string
}

const same = (left: string | undefined, right: string) => left !== undefined && left.toLowerCase() === right.toLowerCase()

/** Contracts whose manifest carries the implemented plan: the implementation itself, the diagnosis, and the ordinary review. */
export const PLAN_BEARING_CONTRACTS = ['ImplementationReport', 'VerificationDiagnosis', 'ImplementationReview'] as const

/**
 * Returns the problems found in the invocation evidence; an empty list means every plan-bearing stage saw the revised Proposal and
 * none saw the Planner root. At least one entry per plan-bearing contract is required, so a missing stage is also a problem.
 */
export function planIdentityProblems(entries: readonly InvocationEntry[], plan: PlanIdentities): string[] {
  const problems: string[] = []
  if (same(plan.rootId, plan.revisedId)) {
    problems.push('The revised Proposal and the Planner root are the same message, so the journey cannot discriminate them.')
  }
  for (const contract of PLAN_BEARING_CONTRACTS) {
    const seen = entries.filter((entry) => entry.contract === contract)
    if (seen.length === 0) {
      problems.push(`No ${contract} stage reached the doubles.`)
    }
    for (const entry of seen) {
      if (same(entry.planMessageId, plan.rootId)) {
        problems.push(`${contract} was given the Planner root instead of the revised Proposal.`)
      } else if (!same(entry.planMessageId, plan.revisedId)) {
        problems.push(`${contract} was given a plan that is neither the revised Proposal nor the Planner root.`)
      }
      if (entry.planMarker !== 'REVISED-PLAN') {
        problems.push(`${contract} did not see the revised plan's content.`)
      }
    }
  }
  return problems
}
