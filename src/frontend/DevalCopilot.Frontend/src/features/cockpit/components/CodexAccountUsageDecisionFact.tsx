import type { CodexAccountUsageDecisionResponse, CodexAccountUsageStopResponse } from '../../../api/clients'
import { describeAccountUsageDecision, describeAttemptAccountUsageStop } from '../describeCodexAccountUsageStop'

interface CodexAccountUsageDecisionFactProps {
  /** The attempt's provider as the evidence response names it; the fact exists only for a Codex attempt. */
  provider?: string
  /** The stop in force when the attempt was claimed; null/undefined when there is no fact. */
  stop?: CodexAccountUsageStopResponse | null
  /** The stored decision of an attempt that was not started; null/undefined when none was recorded. */
  decision?: CodexAccountUsageDecisionResponse | null
  className?: string
}

/**
 * Presents the account-usage stop one Codex attempt was claimed under and, when the attempt was
 * not started because of it, the stored decision with the host retrieval time and reported usage
 * windows. It is a local guard over a provider-reported percentage; an absent decision shows
 * nothing about usage (never "below threshold"), and no provider text is rendered.
 */
export function CodexAccountUsageDecisionFact({ provider, stop, decision, className }: CodexAccountUsageDecisionFactProps) {
  if (provider !== 'Codex') {
    return null
  }

  const stopLine = describeAttemptAccountUsageStop(stop)
  const described = describeAccountUsageDecision(decision)
  const cls = ['dc-codex-account-usage-decision', className].filter(Boolean).join(' ')

  return (
    <section className={cls} aria-label="Codex account-usage stop">
      {stopLine && <p>{stopLine}</p>}
      {described && (
        <>
          <p>{described.headline}</p>
          {described.details.map((line, index) => (
            // eslint-disable-next-line react/no-array-index-key -- lines are a stable-order projection, not a reorderable list
            <p key={index} className="dc-codex-account-usage-decision-detail">
              {line}
            </p>
          ))}
        </>
      )}
      <p className="dc-card-evidence-caveat">
        A local guard over a provider-reported percentage; it says nothing about account access, remaining quota or live capacity.
      </p>
    </section>
  )
}
