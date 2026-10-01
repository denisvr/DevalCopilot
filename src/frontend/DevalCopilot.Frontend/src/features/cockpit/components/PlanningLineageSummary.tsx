import type { PlanningLineage, ReviewStatusHint } from '../derivePlanningLineage'
import { deriveLineageStage } from '../derivePlanningLineage'

interface PlanningLineageSummaryProps {
  lineage: PlanningLineage
  review: ReviewStatusHint
}

const STAGE_TEXT: Record<string, string> = {
  revised:
    'The first challenge round produced a revised proposal. An optional second Claude review of it is available, and the revision may also be implemented directly.',
  reviewRunning: 'The optional second review of the revised proposal is running.',
  reviewAccepted: 'The second review accepted the revised proposal; it may be implemented with that acceptance.',
  reviewChallenged:
    'The second review challenged the revised proposal, so implementing it is blocked. One more explicit Codex resolution can answer every challenge; it is the last round.',
  reviewNotConcluded:
    'The last second-review attempt did not conclude with an acceptance or a challenge; the revised proposal has no second review yet.',
  statusUnavailable: 'The second-review status is not available, so the next step for the revised proposal is not shown.',
  ambiguous: 'More than one revised proposal claims the same parent, so no further review or implementation is offered.',
}

/**
 * Shows where the newest proposal lineage stands after its first challenge round, and truthfully
 * states the end of the lineage: once a second challenge round is resolved there is no third review
 * and the final revised proposal is implementable only through one explicit human authorization — a human decision (or a new planning request)
 * is needed, and the escalation record is never an approval. Renders nothing for a lineage with no
 * revision. Purely a display of what the loaded timeline and the latest review status show; the
 * backend decides every eligibility.
 */
export function PlanningLineageSummary({ lineage, review }: PlanningLineageSummaryProps) {
  const stage = deriveLineageStage(lineage, review)
  if (stage === 'none' || stage === 'root') {
    return null
  }

  return (
    <section className="dc-planning-lineage" aria-label="Proposal lineage">
      {stage === 'exhausted' ? (
        <>
          <p className="dc-planning-lineage-status">
            The second challenge round is resolved. There is no further review or automatic resolution, and the final
            revised proposal cannot be implemented through this lineage without an explicit human authorization, which
            permits at most one implementation claim of it.
          </p>
          {lineage.escalation ? (
            <p className="dc-planning-lineage-escalation" role="status">
              Human decision required: {lineage.escalation.summary} This record is not an approval.
            </p>
          ) : (
            <p className="dc-planning-lineage-escalation" role="status">
              The escalation record is not present in the currently loaded timeline; a human decision is still
              required and this is not an approval.
            </p>
          )}
        </>
      ) : (
        <p className="dc-planning-lineage-status">{STAGE_TEXT[stage]}</p>
      )}
    </section>
  )
}
