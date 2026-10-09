import { useState } from 'react'
import {
  ABANDONED_NOTE,
  ABANDON_EXPLANATION,
  ABANDON_REASON_PROBLEM_COPY,
  abandonIdentityKey,
  describeAbandonmentRefusal,
  validateAbandonReason,
} from '../describeRunAbandonment'
import { useAbandonManualRun } from '../hooks/useAbandonManualRun'
import { useManualRunAbandonment } from '../hooks/useManualRunAbandonment'
import { toUtcText } from '../utcText'

const STATUS_READING = 'Reading the abandonment status…'
const STATUS_FAILED = 'The abandonment status could not be read, so this run cannot be abandoned now.'
const STATUS_REFRESHING = 'The abandonment status is being refreshed; the run cannot be abandoned until it settles.'

interface AbandonRunPanelProps {
  runId: string
  /** The cockpit's latest event sequence; a change re-reads the status. */
  latestSequence: number | undefined
  /** Advanced by the project's explicit Refresh evidence action; re-reads the status. */
  evidenceRefreshGeneration?: number
  /** Re-queries the authoritative cockpit after a recorded abandonment (only for a still-current interaction). */
  onSaved?: () => unknown
  /** Re-reads the project list after a recorded abandonment, so normal intake can offer another objective. */
  onProjectChanged?: () => void
}

/** State that belongs to one committed eligible-form lifetime: replacing it discards all of it, and A to B to A starts fresh. */
interface Owned {
  identityKey: string | null
  draft: string
  draftVersion: number
}

const createOwned = (identityKey: string | null): Owned => ({ identityKey, draft: '', draftVersion: 0 })

/**
 * The explicit abandonment of an inactive manual run (ADR-0031). The form is offered only from a settled, successful status read of
 * this run that is eligible; a pending or failed refresh withholds submission. The draft, the request guard and the error belong to
 * that committed eligible-form lifetime and the draft version, so replacing it, unmounting or an obsolete completion can never act
 * on a replacement, and a request that already reached the host stays real. A recorded abandonment is shown read-only with its
 * persisted reason and time, never as a completion, and an unknown outcome is reconciled only by reading the status again.
 */
export function AbandonRunPanel({ runId, latestSequence, evidenceRefreshGeneration = 0, onSaved, onProjectChanged }: AbandonRunPanelProps) {
  const { status, loading, error: statusError, current, refresh } = useManualRunAbandonment(runId, latestSequence, evidenceRefreshGeneration)
  const recorded = status?.abandonment ?? null
  const eligible = status?.eligible === true && recorded === null
  const identityKey = eligible ? abandonIdentityKey(runId) : null

  const [stored, setStored] = useState(() => createOwned(identityKey))
  let owned = stored
  if (stored.identityKey !== identityKey) {
    owned = createOwned(identityKey)
    setStored(owned)
  }
  const { requesting, error: requestError, submit } = useAbandonManualRun(
    runId,
    identityKey ?? '',
    owned.draftVersion,
    refresh,
    onProjectChanged,
    onSaved,
  )

  const validation = validateAbandonReason(owned.draft)
  const canSubmit = current && eligible && validation.valid && !requesting

  const handleSubmit = () => {
    if (!canSubmit || !validation.valid) {
      return
    }
    void submit(runId, validation.reason)
  }

  let body
  if (statusError) {
    body = <p role="alert">{STATUS_FAILED}</p>
  } else if (!status) {
    body = <p>{loading ? STATUS_READING : STATUS_FAILED}</p>
  } else if (recorded) {
    body = (
      <div className="dc-run-abandoned" data-state="Abandoned">
        {!current && <p>{STATUS_REFRESHING}</p>}
        <p>
          <strong>Abandoned</strong> — not completed.
        </p>
        <dl>
          <dt>Abandoned at</dt>
          <dd>{toUtcText(recorded.abandonedAtUtc)}</dd>
          <dt>Reason</dt>
          <dd className="dc-run-abandoned-reason">{recorded.reason}</dd>
        </dl>
        <p className="dc-run-abandon-note">{ABANDONED_NOTE}</p>
      </div>
    )
  } else if (eligible) {
    body = (
      <>
        <ul className="dc-run-abandon-explanation">
          {ABANDON_EXPLANATION.map((line) => (
            <li key={line}>{line}</li>
          ))}
        </ul>
        {!current && <p>{STATUS_REFRESHING}</p>}
        <label>
          Reason
          <textarea
            value={owned.draft}
            rows={3}
            disabled={requesting}
            aria-invalid={owned.draft !== '' && !validation.valid ? true : undefined}
            onChange={(event) => {
              const draft = event.target.value
              setStored({ ...owned, draft, draftVersion: owned.draftVersion + 1 })
            }}
          />
        </label>
        {owned.draft !== '' && !validation.valid && <p className="dc-run-abandon-error">{ABANDON_REASON_PROBLEM_COPY[validation.problem]}</p>}
        <button type="button" onClick={handleSubmit} disabled={!canSubmit}>
          Abandon run
        </button>
        {requestError && (
          <p className="dc-run-abandon-error" role="alert">
            {requestError}
          </p>
        )}
      </>
    )
  } else {
    body = <p>{describeAbandonmentRefusal(status.refusalCode)}</p>
  }

  return (
    <section className="dc-run-abandon" aria-label="Abandon run">
      <h3>Abandon run</h3>
      {body}
    </section>
  )
}
