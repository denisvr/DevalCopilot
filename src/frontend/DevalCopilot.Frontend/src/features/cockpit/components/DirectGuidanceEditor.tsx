import { useId, useRef, useState } from 'react'
import { DIRECT_GUIDANCE_MAX_LENGTH, checkDirectGuidanceDraft, directGuidanceLength } from '../describeDirectGuidance'
import { useOwnedFlow } from '../hooks/useRunActionLifetime'

interface DirectGuidanceEditorProps {
  /** With `sourceId`, the identity that owns the draft; returning to an earlier identity is a new one. */
  runId: string
  /** The authoritative source the request is made from (plan proposal message or review attempt). */
  sourceId: string
  /** Accessible names, so the two cockpit actions stay distinguishable. */
  label: string
  formLabel: string
  submitLabel: string
  pendingLabel: string
  /** True while this control's submission is in flight (already scoped to run and source by the hook). */
  requesting: boolean
  statusLoading: boolean
  /** Sends the source together with the raw draft. Resolves true only when the server accepted it AND the
   * submission still belonged to the current lifetime. */
  onSubmit: (guidance: string) => Promise<boolean>
}

const VALIDATION_TEXT = {
  tooLong: `Guidance must be at most ${DIRECT_GUIDANCE_MAX_LENGTH} characters.`,
  control: 'Guidance must not contain control characters other than line breaks.',
} as const

/**
 * Optional direct human guidance submitted together with one explicit mutation request. A blank draft
 * means "no guidance": the plain request button (owned by the caller) sends none, and this form's
 * submit is enabled only for a valid, non-blank draft, so a blank or whitespace-only guidance is never
 * sent. The draft lives only in this component's state and belongs to (run, source): a change of either
 * resets it during render, so nothing carries between identities even without a parent `key`. Every
 * edit, including one that yields identical text, bumps a version; an accepted submission clears the
 * draft only if it still belongs to the same identity, no newer submission began, and the draft was not
 * edited since it was sent. Local checks are feedback only; the server stays authoritative.
 */
export function DirectGuidanceEditor({
  runId,
  sourceId,
  label,
  formLabel,
  submitLabel,
  pendingLabel,
  requesting,
  statusLoading,
  onSubmit,
}: DirectGuidanceEditorProps) {
  const baseId = useId()
  const beginFlow = useOwnedFlow(runId, sourceId)
  const draftVersion = useRef(0)
  // The ownership check of the submission currently in flight, which blocks a synchronous duplicate.
  const inFlight = useRef<(() => boolean) | null>(null)
  const identity = JSON.stringify([runId, sourceId])
  const [seenIdentity, setSeenIdentity] = useState(identity)
  const [draft, setDraft] = useState('')
  if (seenIdentity !== identity) {
    setSeenIdentity(identity)
    setDraft('')
  }

  const check = checkDirectGuidanceDraft(draft)
  const message = !check.ok && check.reason !== 'blank' ? VALIDATION_TEXT[check.reason] : null

  return (
    <form
      className="dc-direct-guidance-editor"
      aria-label={formLabel}
      onSubmit={(event) => {
        event.preventDefault()
        if (!check.ok || requesting || statusLoading) {
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
      <label htmlFor={`${baseId}-text`}>{label}</label>
      <textarea
        id={`${baseId}-text`}
        value={draft}
        rows={4}
        aria-describedby={`${baseId}-help ${baseId}-count`}
        aria-invalid={message ? true : undefined}
        onChange={(event) => {
          draftVersion.current += 1
          setDraft(event.target.value)
        }}
      />
      <p id={`${baseId}-count`}>
        {directGuidanceLength(draft)} / {DIRECT_GUIDANCE_MAX_LENGTH} characters
      </p>
      <p id={`${baseId}-help`}>
        This is sent with the request as advisory context and recorded with the attempt. It cannot change the
        objective, permissions, tools, or budgets. It is not a statement that the provider will follow it, and it is
        not screened for secrets: do not include sensitive data.
      </p>
      {message && <p className="dc-direct-guidance-validation">{message}</p>}
      <button type="submit" disabled={!check.ok || requesting || statusLoading}>
        {requesting ? pendingLabel : submitLabel}
      </button>
    </form>
  )
}
