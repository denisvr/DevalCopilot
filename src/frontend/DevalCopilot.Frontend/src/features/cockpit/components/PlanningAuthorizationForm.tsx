import { useId, useRef, useState } from 'react'
import { DIRECT_GUIDANCE_MAX_LENGTH, checkDirectGuidanceDraft, directGuidanceLength } from '../describeDirectGuidance'
import { useOwnedFlow } from '../hooks/useRunActionLifetime'

interface PlanningAuthorizationFormProps {
  /** With `escalationMessageId` and `finalProposalMessageId`, the identity that owns the draft; returning to an earlier identity is a new one. */
  runId: string
  escalationMessageId: string
  finalProposalMessageId: string
  /** True while this control's submission is in flight (already scoped to run and escalation by the hook). */
  authorizing: boolean
  /** True while the authoritative facts are being read; a submission is withheld meanwhile. */
  statusLoading: boolean
  /** Sends the raw rationale. Resolves true only when the server accepted it AND the submission still belonged to the
   * current lifetime. */
  onSubmit: (rationale: string) => Promise<boolean>
}

const VALIDATION_TEXT = {
  blank: 'A reason is required.',
  tooLong: `The reason must be at most ${DIRECT_GUIDANCE_MAX_LENGTH} characters.`,
  control: 'The reason must not contain control characters other than line breaks.',
} as const

/**
 * The required human reason for the one explicit authorization of an escalated final plan. The draft lives only in
 * this component's state and belongs to (run, escalation, final plan): a change of any resets it during render, so
 * nothing carries between identities even without a parent `key`. Every edit, including one that yields identical
 * text, bumps a version; an accepted submission clears the draft only if it still belongs to the same identity, no
 * newer submission began, and the draft was not edited since it was sent. A synchronous second submit while one is in
 * flight is ignored. Local checks are feedback only; the server stays authoritative and the text is advisory context.
 */
export function PlanningAuthorizationForm({
  runId,
  escalationMessageId,
  finalProposalMessageId,
  authorizing,
  statusLoading,
  onSubmit,
}: PlanningAuthorizationFormProps) {
  const baseId = useId()
  const identity = JSON.stringify([escalationMessageId, finalProposalMessageId])
  const beginFlow = useOwnedFlow(runId, identity)
  const draftVersion = useRef(0)
  // The ownership check of the submission currently in flight, which blocks a synchronous duplicate.
  const inFlight = useRef<(() => boolean) | null>(null)
  const ownerKey = JSON.stringify([runId, identity])
  const [seenOwner, setSeenOwner] = useState(ownerKey)
  const [draft, setDraft] = useState('')
  const [showValidation, setShowValidation] = useState(false)
  if (seenOwner !== ownerKey) {
    setSeenOwner(ownerKey)
    setDraft('')
    setShowValidation(false)
  }

  const check = checkDirectGuidanceDraft(draft)
  const message = !check.ok ? VALIDATION_TEXT[check.reason] : null

  return (
    <form
      className="dc-planning-authorization-form"
      aria-label="Authorize one implementation claim"
      onSubmit={(event) => {
        event.preventDefault()
        if (authorizing || statusLoading) {
          return
        }
        if (!check.ok) {
          setShowValidation(true)
          return
        }
        if (inFlight.current?.()) {
          return
        }
        const owns = beginFlow()
        if (!owns()) {
          return
        }
        inFlight.current = owns
        const versionAtSubmit = draftVersion.current
        const settle = () => {
          if (inFlight.current === owns) {
            inFlight.current = null
          }
        }
        Promise.resolve(onSubmit(draft)).then(
          (accepted) => {
            settle()
            if (accepted && owns() && draftVersion.current === versionAtSubmit) {
              setDraft('')
            }
          },
          settle,
        )
      }}
    >
      <label htmlFor={`${baseId}-text`}>Your reason for authorizing this final plan (required)</label>
      <textarea
        id={`${baseId}-text`}
        value={draft}
        rows={4}
        aria-describedby={`${baseId}-help ${baseId}-count`}
        aria-invalid={showValidation && message ? true : undefined}
        onChange={(event) => {
          draftVersion.current += 1
          setDraft(event.target.value)
          setShowValidation(false)
        }}
      />
      <p id={`${baseId}-count`}>
        {directGuidanceLength(draft)} / {DIRECT_GUIDANCE_MAX_LENGTH} characters
      </p>
      <p id={`${baseId}-help`}>
        This reason is recorded in the collaboration timeline and sent to the Implementer as advisory context if you
        later request the implementation. It cannot change the plan, objective, permissions, tools, or budgets. It is
        not screened for secrets: do not include sensitive data.
      </p>
      {showValidation && message && (
        <p role="status" className="dc-planning-authorization-validation">
          {message}
        </p>
      )}
      <button type="submit" disabled={authorizing || statusLoading}>
        {authorizing ? 'Authorizing…' : 'Authorize one implementation claim'}
      </button>
    </form>
  )
}
