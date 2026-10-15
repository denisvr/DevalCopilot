import { useMemo } from 'react'
import { PUBLICATION_STATEMENTS } from '../gitHubBranchPublicationApproval'
import type { ObservationScope, ObservationSource } from '../gitHubObservationTarget'
import { useGitHubBranchPublicationApproval } from '../hooks/useGitHubBranchPublicationApproval'
import { toUtcText } from '../utcText'

const LOAD_FAILURES = {
  read: 'The recorded GitHub branch publication approval could not be read.',
  invalid: 'The host answered with a result that is not coherent for this delivery, so none of it is shown.',
} as const

const WRITE_FAILURES = {
  refused:
    'The host refused this approval (the recorded facts may have changed, or a different approval may already be recorded). Nothing was changed by this request. Reload to read what is recorded before approving again.',
  ambiguous:
    'The outcome of approving is unknown: the host may have recorded the approval. Reload to read what was recorded before approving again.',
} as const

interface GitHubBranchPublicationApprovalPanelProps {
  /** The committed project of the run; part of what owns every read and approval of this region. */
  projectId: string
  /** The exact completed local delivery of the recorded target. */
  source: ObservationSource
  /** The immutable recorded target, already proven coherent for `source`. */
  target: ObservationScope
}

/**
 * Explicit, collapsed region under a recorded GitHub observation target for the Human's approval of ONE future normal push of the exact
 * local commit to its exact branch (ADR-0048). Showing it sends nothing. It reads only on the explicit load or reload and writes only
 * through the separate approve action, which is armed only by the current settled "no approval recorded" read of this opening. It shows
 * the destination, the full reference and the commit before anything is recorded, together with a fixed statement that no push occurs
 * now and that nothing else is authorized, and afterwards the recorded consent, never an execution action. Every value is inert, bounded
 * text with UTC times: never a link, copy or other action. Recorded consent is not a check of GitHub and not readiness to push.
 */
export function GitHubBranchPublicationApprovalPanel({ projectId, source, target }: GitHubBranchPublicationApprovalPanelProps) {
  const scope = useMemo(() => ({ projectId, source, target }), [projectId, source, target])
  const { open, load, write, canApprove, show, close, reload, approve } = useGitHubBranchPublicationApproval(scope)
  const pending = load.kind === 'pending' || write.kind === 'pending'
  const reading = load.kind === 'result' ? load.reading : null

  return (
    <section className="dc-github-approval" aria-label="GitHub branch publication approval">
      {!open ? (
        <button type="button" onClick={show}>
          Show GitHub branch publication approval
        </button>
      ) : (
        <div className="dc-github-approval-body">
          <h5>GitHub branch publication approval</h5>
          <p className="dc-github-approval-note">{PUBLICATION_STATEMENTS.Consent}</p>
          <dl className="dc-github-approval-scope">
            <dt>Destination</dt>
            <dd>
              github.com / {target.owner} / {target.repository}
            </dd>
            {reading?.kind === 'ApprovalNotRecorded' ? (
              <>
                <dt>Bound repository ID</dt>
                <dd>{reading.binding.repositoryNodeId}</dd>
              </>
            ) : null}
            <dt>Reference</dt>
            <dd>refs/heads/{source.branchName}</dd>
            <dt>Local commit</dt>
            <dd>{source.commitSha}</dd>
          </dl>
          <p>
            <button type="button" onClick={reload} disabled={pending}>
              {load.kind === 'idle' ? 'Load GitHub branch publication approval' : 'Reload GitHub branch publication approval'}
            </button>{' '}
            {reading?.kind !== 'Available' ? (
              <>
                <button type="button" onClick={approve} disabled={!canApprove}>
                  Approve one future push
                </button>{' '}
              </>
            ) : null}
            <button type="button" onClick={close}>
              Close GitHub branch publication approval
            </button>
          </p>
          {load.kind === 'idle' ? <p className="dc-github-approval-state">Not loaded.</p> : null}
          {load.kind === 'pending' ? <p className="dc-github-approval-state">Reading the recorded approval…</p> : null}
          {load.kind === 'failed' ? (
            <p className="dc-github-approval-state" role="alert">
              {LOAD_FAILURES[load.reason]}
            </p>
          ) : null}
          {reading?.kind === 'Unavailable' ? (
            <p className="dc-github-approval-state" data-status="Unavailable">
              {PUBLICATION_STATEMENTS.Unavailable}
            </p>
          ) : null}
          {reading?.kind === 'NotRecorded' ? (
            <p className="dc-github-approval-state" data-status="NotRecorded">
              {PUBLICATION_STATEMENTS.NotRecorded}
            </p>
          ) : null}
          {reading?.kind === 'ApprovalNotRecorded' ? (
            <p className="dc-github-approval-state" data-status="ApprovalNotRecorded">
              {PUBLICATION_STATEMENTS.ApprovalNotRecorded}
            </p>
          ) : null}
          {reading?.kind === 'Available' ? (
            <div className="dc-github-approval-result" data-status="Available">
              <p className="dc-github-approval-state">{PUBLICATION_STATEMENTS.Available}</p>
              <dl className="dc-github-approval-recorded">
                <dt>Destination</dt>
                <dd>
                  github.com / {target.owner} / {target.repository}
                </dd>
                <dt>Repository ID</dt>
                <dd>{reading.binding.repositoryNodeId}</dd>
                <dt>Reference</dt>
                <dd>refs/heads/{source.branchName}</dd>
                <dt>Commit</dt>
                <dd>{source.commitSha}</dd>
                <dt>Approved (UTC)</dt>
                <dd>{toUtcText(reading.approval.approvedAtUtc)}</dd>
                <dt>Approval</dt>
                <dd>{reading.approval.id}</dd>
                <dt>Approval event</dt>
                <dd>{reading.approval.eventId}</dd>
              </dl>
            </div>
          ) : null}
          {write.kind === 'pending' ? <p className="dc-github-approval-state">Recording the approval…</p> : null}
          {write.kind === 'failed' ? (
            <p className="dc-github-approval-state" role="alert">
              {WRITE_FAILURES[write.reason]}
            </p>
          ) : null}
        </div>
      )}
    </section>
  )
}
