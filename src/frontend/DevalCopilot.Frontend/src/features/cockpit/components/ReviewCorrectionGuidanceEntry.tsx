import { useState } from 'react'

/** Mirrors `ReviewCorrectionGuidance.MaximumLength`; the server stays authoritative. */
export const MAXIMUM_GUIDANCE_LENGTH = 600

interface ReviewCorrectionGuidanceEntryProps {
  authorizing: boolean
  statusLoading: boolean
  /** Sends the raw draft to the guided operation. Resolves true only when the server accepted it. */
  onSubmit: (guidance: string) => Promise<boolean>
}

/** The server's normalization, previewed only to give early feedback: line endings to `\n`, trimmed. */
function normalizeForLocalCheck(draft: string): string {
  return draft.replace(/\r\n?/g, '\n').trim()
}

/**
 * Optional short human guidance for the one explicit authorization of an additional review
 * correction. The draft lives only in this component's state — never browser storage or the URL — and
 * is discarded when the component unmounts or its `key` (the escalation) changes. Local checks are
 * feedback only: the server normalizes, bounds, screens, and decides eligibility, and its fixed safe
 * refusal is shown by the caller. The text is advisory context recorded in the collaboration
 * timeline; it is not screened for secrets, so the form says not to include any.
 */
export function ReviewCorrectionGuidanceEntry({ authorizing, statusLoading, onSubmit }: ReviewCorrectionGuidanceEntryProps) {
  const [draft, setDraft] = useState('')
  const [showValidation, setShowValidation] = useState(false)
  const normalized = normalizeForLocalCheck(draft)
  const validationMessage =
    normalized.length === 0
      ? 'Enter guidance, or use the plain authorization above.'
      : normalized.length > MAXIMUM_GUIDANCE_LENGTH
        ? `Guidance must be at most ${MAXIMUM_GUIDANCE_LENGTH} characters.`
        : null

  return (
    <form
      className="dc-review-correction-guidance"
      aria-label="Authorize with guidance"
      onSubmit={(event) => {
        event.preventDefault()
        if (validationMessage) {
          setShowValidation(true)
          return
        }
        void onSubmit(draft).then((accepted) => {
          if (accepted) setDraft('')
        })
      }}
    >
      <label htmlFor="dc-review-correction-guidance-text">Optional guidance for the correction</label>
      <textarea
        id="dc-review-correction-guidance-text"
        value={draft}
        rows={4}
        disabled={authorizing}
        aria-describedby="dc-review-correction-guidance-help dc-review-correction-guidance-count"
        aria-invalid={showValidation && validationMessage ? true : undefined}
        onChange={(event) => {
          setDraft(event.target.value)
          setShowValidation(false)
        }}
      />
      <p id="dc-review-correction-guidance-count">
        {normalized.length} / {MAXIMUM_GUIDANCE_LENGTH} characters
      </p>
      <p id="dc-review-correction-guidance-help">
        This is sent to the Implementer as advisory context and recorded in the collaboration timeline. It cannot
        change the objective, the findings, permissions, tools, or budgets. It is not screened for secrets: do not
        include credentials or sensitive data.
      </p>
      {showValidation && validationMessage && (
        <p role="status" className="dc-review-correction-guidance-validation">
          {validationMessage}
        </p>
      )}
      <button type="submit" disabled={authorizing || statusLoading}>
        {authorizing ? 'Authorizing…' : 'Authorize with guidance'}
      </button>
    </form>
  )
}
