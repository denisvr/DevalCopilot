import { useState } from 'react'
import type { GetLocalCommitStatusResponse, LocalCommitOperationResponse } from '../../../api/clients'
import {
  LOCAL_COMMIT_MESSAGE_PROBLEM_COPY,
  LOCAL_COMMIT_STATUS_COPY,
  describeLocalCommitOutcomeReason,
  describeLocalCommitRefusal,
  localCommitIdentityKey,
  validateLocalCommitMessage,
} from '../describeLocalCommit'
import type { LocalCommitIdentity } from '../describeLocalCommit'
import { useLocalCommitStatus } from '../hooks/useLocalCommitStatus'
import { useRequestLocalCommit } from '../hooks/useRequestLocalCommit'

const EXPLANATION = [
  'The commit is local only: nothing is pushed.',
  'It is unsigned and is created without running any Git hooks, using your configured Git author.',
  'Admitting it is irreversible for this run: a run has one local-commit operation, and it is never retried automatically.',
  'It is refused rather than converted if a Git setting or attribute would change the approved bytes (for example line-ending normalization); normalize the files in a new checkpoint instead.',
]
const STATUS_READING = 'Reading the local-commit status…'
const STATUS_FAILED = 'The local-commit status could not be read, so a local commit cannot be requested now.'
const STATUS_REFRESHING = 'The local-commit status is being refreshed; a local commit cannot be requested until it settles.'
const SYNC_NOTE = 'Local commit only — not pushed. This does not describe the state of the rest of the workspace.'

interface LocalCommitPanelProps {
  runId: string
  /** The cockpit's latest event sequence; a change re-reads the status. */
  latestSequence: number | undefined
  /** Advanced by the project's explicit Refresh evidence action; re-reads the status. */
  evidenceRefreshGeneration?: number
  /** Re-queries the authoritative cockpit after an admitted request. */
  onSaved?: () => unknown
}

/** State that belongs to one committed approval identity: replacing the identity discards all of it, and A to B to A starts fresh. */
interface Owned {
  identityKey: string | null
  draft: string
  draftVersion: number
  operationId: string
}

const createOwned = (identityKey: string | null): Owned => ({
  identityKey,
  draft: '',
  draftVersion: 0,
  operationId: identityKey === null ? '' : crypto.randomUUID(),
})

function identityOf(status: GetLocalCommitStatusResponse | null): LocalCommitIdentity | null {
  if (!status || status.eligible !== true || status.operation) {
    return null
  }
  const { checkpointId, codeReviewAttemptId, humanCheckpointReviewId } = status
  return checkpointId && codeReviewAttemptId && humanCheckpointReviewId ? { checkpointId, codeReviewAttemptId, humanCheckpointReviewId } : null
}

function OperationCard({ operation }: { operation: LocalCommitOperationResponse }) {
  const status = operation.status ?? ''
  const reason = describeLocalCommitOutcomeReason(operation.outcomeReasonCode)
  return (
    <div className="dc-local-commit-operation" data-status={status}>
      <p>
        <strong>{status || 'Unknown status'}</strong>
        {' — '}
        {LOCAL_COMMIT_STATUS_COPY[status] ?? 'The host recorded a status this version does not describe.'}
      </p>
      {reason && <p>Recorded reason: {reason}</p>}
      <dl>
        {operation.checkpointNumber !== undefined && (
          <>
            <dt>Checkpoint</dt>
            <dd>#{operation.checkpointNumber}</dd>
          </>
        )}
        {operation.branchName && (
          <>
            <dt>Branch</dt>
            <dd>{operation.branchName}</dd>
          </>
        )}
        {operation.parentCommitSha && (
          <>
            <dt>Parent</dt>
            <dd>{operation.parentCommitSha}</dd>
          </>
        )}
        {operation.treeSha && (
          <>
            <dt>Tree</dt>
            <dd>{operation.treeSha}</dd>
          </>
        )}
        {operation.changedPathCount !== undefined && (
          <>
            <dt>Changed paths</dt>
            <dd>{operation.changedPathCount}</dd>
          </>
        )}
        {status === 'Completed' && operation.commitSha && (
          <>
            <dt>Local commit</dt>
            <dd>{operation.commitSha}</dd>
          </>
        )}
      </dl>
      {status === 'Completed' && <p className="dc-local-commit-note">{SYNC_NOTE}</p>}
    </div>
  )
}

/**
 * The explicit local commit of the approved checkpoint (ADR-0029). The form is offered only from a settled, successful status read
 * for this run that names an eligible approval identity; a pending or failed refresh withholds submission. Draft, request guard,
 * error and the operation id belong to that committed identity and the draft version, so replacing the identity, unmounting or an
 * obsolete completion can never act on a replacement. The recorded operation is shown read-only and never implies a push, a clean
 * workspace or provider reliability.
 */
export function LocalCommitPanel({ runId, latestSequence, evidenceRefreshGeneration = 0, onSaved }: LocalCommitPanelProps) {
  const { status, loading, error: statusError, current, refresh } = useLocalCommitStatus(runId, latestSequence, evidenceRefreshGeneration)
  const identity = identityOf(status)
  const identityKey = identity ? localCommitIdentityKey(runId, identity) : null

  const [stored, setStored] = useState(() => createOwned(identityKey))
  let owned = stored
  if (stored.identityKey !== identityKey) {
    owned = createOwned(identityKey)
    setStored(owned)
  }
  const { requesting, error: requestError, submit } = useRequestLocalCommit(runId, identityKey ?? '', owned.draftVersion, refresh, onSaved)

  const validation = validateLocalCommitMessage(owned.draft)
  const canSubmit = current && identity !== null && validation.valid && !requesting

  const handleSubmit = () => {
    if (!canSubmit || !identity) {
      return
    }
    void submit(runId, identity, validation.message, owned.operationId)
  }

  let body
  if (statusError) {
    body = <p role="alert">{STATUS_FAILED}</p>
  } else if (!status) {
    body = <p>{loading ? STATUS_READING : STATUS_FAILED}</p>
  } else if (status.operation) {
    body = (
      <>
        {!current && <p>{STATUS_REFRESHING}</p>}
        <OperationCard operation={status.operation} />
      </>
    )
  } else if (identity) {
    body = (
      <>
        <ul className="dc-local-commit-explanation">
          {EXPLANATION.map((line) => (
            <li key={line}>{line}</li>
          ))}
        </ul>
        {!current && <p>{STATUS_REFRESHING}</p>}
        <label>
          Commit message
          <textarea
            value={owned.draft}
            rows={4}
            disabled={requesting}
            aria-invalid={owned.draft !== '' && !validation.valid ? true : undefined}
            onChange={(event) => {
              const draft = event.target.value
              setStored({ ...owned, draft, draftVersion: owned.draftVersion + 1 })
            }}
          />
        </label>
        {owned.draft !== '' && !validation.valid && <p className="dc-local-commit-error">{LOCAL_COMMIT_MESSAGE_PROBLEM_COPY[validation.problem]}</p>}
        <button type="button" onClick={handleSubmit} disabled={!canSubmit}>
          Commit locally
        </button>
        {requestError && (
          <p className="dc-local-commit-error" role="alert">
            {requestError}
          </p>
        )}
      </>
    )
  } else {
    body = <p>{describeLocalCommitRefusal(status.refusalCode)}</p>
  }

  return (
    <section className="dc-local-commit" aria-label="Local commit">
      <h3>Local commit</h3>
      {body}
    </section>
  )
}
