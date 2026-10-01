import type { DirectHumanGuidanceResponse } from '../../../api/clients'
import { describeDirectGuidance } from '../describeDirectGuidance'

interface DirectGuidanceFactProps {
  /** The attempt's immutable direct-guidance fact; null/undefined for no attempt or a non-mutation path. */
  fact?: DirectHumanGuidanceResponse | null
  className?: string
}

/**
 * Presents the direct human guidance the host supplied to one attempt's sealed context. It is a
 * statement about the input only, never that a provider followed it, and it is separate from the
 * guidance recorded with an additional-correction authorization. The text is rendered as plain React
 * text (never markup) with line breaks preserved by CSS, and `Unknown` never shows any text.
 */
export function DirectGuidanceFact({ fact, className }: DirectGuidanceFactProps) {
  const display = describeDirectGuidance(fact)
  if (display === null) {
    return null
  }

  const cls = ['dc-direct-guidance-fact', className].filter(Boolean).join(' ')
  switch (display.kind) {
    case 'provided':
      return (
        <div className={cls}>
          <p>Direct human guidance supplied to this attempt (host-supplied input; whether the provider followed it is not observed):</p>
          <p className="dc-direct-guidance-text">{display.text}</p>
        </div>
      )
    case 'notRecorded':
      return (
        <p className={cls}>
          Direct human guidance for this attempt: none recorded.
        </p>
      )
    default:
      return (
        <p className={cls}>Direct human guidance for this attempt: unknown (the recorded facts disagree); no text is shown.</p>
      )
  }
}
