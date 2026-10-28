import { useMemo } from 'react'
import { useLocalCommitInspection } from '../hooks/useLocalCommitInspection'
import type { LocalCommitInspectionOperation } from '../localCommitInspection'
import {
  LOCAL_COMMIT_INSPECTION_COMMIT_NOTE,
  LOCAL_COMMIT_INSPECTION_FAILURES,
  LOCAL_COMMIT_INSPECTION_HIDE_LABEL,
  LOCAL_COMMIT_INSPECTION_NOTE,
  LOCAL_COMMIT_INSPECTION_NOT_RECORDED,
  LOCAL_COMMIT_INSPECTION_NO_COMMIT_NOTE,
  LOCAL_COMMIT_INSPECTION_NO_START,
  LOCAL_COMMIT_INSPECTION_PREPARATION_NOTE,
  LOCAL_COMMIT_INSPECTION_READING,
  LOCAL_COMMIT_INSPECTION_REASON_EXPLANATION,
  LOCAL_COMMIT_INSPECTION_REASON_NONE,
  LOCAL_COMMIT_INSPECTION_REASON_UNRECOGNIZED,
  LOCAL_COMMIT_INSPECTION_REGION_LABEL,
  LOCAL_COMMIT_INSPECTION_RELOAD_LABEL,
  LOCAL_COMMIT_INSPECTION_STATUS_DESCRIPTION,
  LOCAL_COMMIT_INSPECTION_STATUS_LABEL,
  LOCAL_COMMIT_INSPECTION_UNAVAILABLE,
} from '../localCommitInspectionCopy'

function reasonText(operation: LocalCommitInspectionOperation): string {
  const { reason } = operation
  if (reason.kind === 'Known') {
    return `${reason.code} · ${LOCAL_COMMIT_INSPECTION_REASON_EXPLANATION[reason.code]}`
  }

  return reason.kind === 'Unrecognized' ? LOCAL_COMMIT_INSPECTION_REASON_UNRECOGNIZED : LOCAL_COMMIT_INSPECTION_REASON_NONE
}

function OperationView({ operation }: { operation: LocalCommitInspectionOperation }) {
  return (
    <div className="dc-local-commit-inspection-operation">
      <dl>
        <div>
          <dt>Recorded status</dt>
          <dd>{LOCAL_COMMIT_INSPECTION_STATUS_LABEL[operation.status]}</dd>
        </div>
        <div>
          <dt>Recorded reason</dt>
          <dd>{reasonText(operation)}</dd>
        </div>
        <div>
          <dt>Recorded times (UTC)</dt>
          <dd>
            Prepared {operation.createdAtUtc} · execution marker {operation.executionStartedAtUtc ?? 'not recorded'} · outcome recorded{' '}
            {operation.completedAtUtc ?? 'not recorded'}
          </dd>
        </div>
        <div>
          <dt>Recorded branch</dt>
          <dd>{operation.branchName}</dd>
        </div>
        <div>
          <dt>Recorded preparation facts</dt>
          <dd>
            parent commit {operation.parentCommitSha} · tree {operation.treeSha}
          </dd>
        </div>
        <div>
          <dt>Recorded result commit</dt>
          <dd>{operation.commitSha ?? 'not disclosed'}</dd>
        </div>
        <div>
          <dt>Recorded change</dt>
          <dd>
            {operation.changedPathCount} changed {operation.changedPathCount === 1 ? 'path' : 'paths'} · {operation.totalBytes} bytes
          </dd>
        </div>
        <div>
          <dt>Recorded checkpoint</dt>
          <dd>
            #{operation.checkpointNumber} · {operation.checkpointId} · fingerprint {operation.checkpointFingerprintSha256}
          </dd>
        </div>
        <div>
          <dt>Recorded identities</dt>
          <dd>
            operation {operation.operationId} · project {operation.projectId} · workspace {operation.workspaceId}
          </dd>
        </div>
      </dl>
      <p className="dc-local-commit-note">{LOCAL_COMMIT_INSPECTION_STATUS_DESCRIPTION[operation.status]}</p>
      {operation.executionStartedAtUtc === null && operation.status !== 'Prepared' ? (
        <p className="dc-local-commit-note">{LOCAL_COMMIT_INSPECTION_NO_START}</p>
      ) : null}
      <p className="dc-local-commit-note">{LOCAL_COMMIT_INSPECTION_PREPARATION_NOTE}</p>
      <p className="dc-local-commit-note">
        {operation.commitSha === null ? LOCAL_COMMIT_INSPECTION_NO_COMMIT_NOTE : LOCAL_COMMIT_INSPECTION_COMMIT_NOTE}
      </p>
    </div>
  )
}

interface LocalCommitInspectionRegionProps {
  runId: string
  /** The project the run belongs to, when the surface knows it: a region of another project or run never shares state with this one. */
  projectId: string | null
}

/**
 * The collapsed, explicit **Inspect recorded local commit** region of one run, beside the current run's local-commit panel and inside a
 * selected historical run alike, with or without a delivery receipt source (ADR-0070). Opening or reloading reads the recorded operation
 * once; a closed region reads nothing, and nothing polls, retries or refreshes it. It describes what was recorded, never a live process,
 * the repository, current eligibility or publication, and it mounts no request, retry, recovery, release, approval, commit, publication,
 * export or receipt control and does not duplicate the delivery receipt. Every host value is rendered as literal text.
 */
export function LocalCommitInspectionRegion({ runId, projectId }: LocalCommitInspectionRegionProps) {
  const ownerKey = useMemo(() => JSON.stringify([projectId, runId]), [projectId, runId])
  const target = useMemo(() => ({ runId }), [runId])
  const { open, pending, answer, failure, show, close, reload } = useLocalCommitInspection(ownerKey, target)

  return (
    <section className="dc-local-commit-inspection" aria-label={LOCAL_COMMIT_INSPECTION_REGION_LABEL}>
      <button type="button" aria-expanded={open} onClick={open ? close : show}>
        {open ? LOCAL_COMMIT_INSPECTION_HIDE_LABEL : LOCAL_COMMIT_INSPECTION_REGION_LABEL}
      </button>
      {open ? (
        <div className="dc-local-inspection-body">
          {LOCAL_COMMIT_INSPECTION_NOTE.map((line) => (
            <p key={line} className="dc-local-commit-note">
              {line}
            </p>
          ))}
          <p>
            <button type="button" onClick={reload}>
              {LOCAL_COMMIT_INSPECTION_RELOAD_LABEL}
            </button>
          </p>
          {pending ? <p className="dc-empty-state">{LOCAL_COMMIT_INSPECTION_READING}</p> : null}
          {failure !== null ? (
            <p className="dc-card-evidence-error" role="alert">
              {LOCAL_COMMIT_INSPECTION_FAILURES[failure]}
            </p>
          ) : null}
          {answer?.status === 'NotRecorded' ? (
            <p className="dc-empty-state" role="status">
              {LOCAL_COMMIT_INSPECTION_NOT_RECORDED}
            </p>
          ) : null}
          {answer?.status === 'Unavailable' ? (
            <p className="dc-empty-state" role="status">
              {LOCAL_COMMIT_INSPECTION_UNAVAILABLE}
            </p>
          ) : null}
          {answer?.status === 'Recorded' ? <OperationView operation={answer.operation} /> : null}
        </div>
      ) : null}
    </section>
  )
}
