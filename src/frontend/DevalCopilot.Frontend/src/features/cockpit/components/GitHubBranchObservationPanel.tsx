import { useMemo } from 'react'
import { BRANCH_OBSERVATION_STATEMENTS } from '../gitHubBranchObservation'
import type { ObservationScope, ObservationSource } from '../gitHubObservationTarget'
import { useGitHubBranchObservation } from '../hooks/useGitHubBranchObservation'
import { toUtcText } from '../utcText'

const CHECK_NOTE =
  'Check queries GitHub (github.com) through the GitHub CLI, using its default stored authentication, for exactly the recorded owner and repository and the recorded branch above. Only the repository ID, its names and the commit that branch points at are read. A match is only what GitHub reported at that moment: it is not a CI result, a permission, a durable binding, a publication approval or a publication performed by DevalCopilot, and it says nothing about the future. Nothing is checked until you ask, and nothing is repeated automatically.'

const FAILURES = {
  read: 'The GitHub remote branch could not be read.',
  invalid: 'The host answered with a result that is not coherent for this recorded target, so none of it is shown.',
} as const

interface GitHubBranchObservationPanelProps {
  /** The exact completed local delivery of the recorded target. */
  source: ObservationSource
  /** The immutable recorded target, already proven coherent for `source`. */
  target: ObservationScope
}

/**
 * Explicit, collapsed remote branch region beside an Available GitHub observation target (ADR-0042), separate from the identity region.
 * It reads only on the explicit check or recheck, shows the exact local source and recorded scope before anything is checked, and renders
 * the answer as inert, bounded text: the returned branch and commit, the repository identity and the host's observation time, never a
 * link, copy or other action. Nothing is read on mount, opening, a project, run or target change, a reconnect or a timer.
 */
export function GitHubBranchObservationPanel({ source, target }: GitHubBranchObservationPanelProps) {
  const scope = useMemo(() => ({ source, target }), [source, target])
  const { open, phase, show, close, check } = useGitHubBranchObservation(scope)
  const reading = phase.kind === 'result' ? phase.reading : null

  return (
    <section className="dc-github-branch" aria-label="GitHub remote branch">
      {!open ? (
        <button type="button" onClick={show}>
          Show GitHub remote branch
        </button>
      ) : (
        <div className="dc-github-branch-body">
          <h5>GitHub remote branch</h5>
          <p className="dc-github-branch-note">{CHECK_NOTE}</p>
          <dl className="dc-github-branch-scope">
            <dt>Recorded branch</dt>
            <dd>{source.branchName}</dd>
            <dt>Recorded local commit</dt>
            <dd>{source.commitSha}</dd>
            <dt>Recorded owner</dt>
            <dd>{target.owner}</dd>
            <dt>Recorded repository</dt>
            <dd>{target.repository}</dd>
          </dl>
          <p>
            <button type="button" onClick={check} disabled={phase.kind === 'pending'}>
              {phase.kind === 'idle' ? 'Check GitHub remote branch' : 'Recheck GitHub remote branch'}
            </button>{' '}
            <button type="button" onClick={close}>
              Close GitHub remote branch
            </button>
          </p>
          {phase.kind === 'idle' ? <p className="dc-github-branch-state">Not checked.</p> : null}
          {phase.kind === 'pending' ? <p className="dc-github-branch-state">Querying GitHub for the remote branch…</p> : null}
          {phase.kind === 'failed' ? (
            <p className="dc-github-branch-state" role="alert">
              {FAILURES[phase.reason]}
            </p>
          ) : null}
          {reading && !('facts' in reading) ? (
            <p className="dc-github-branch-state" data-status={reading.status}>
              {BRANCH_OBSERVATION_STATEMENTS[reading.status]}
            </p>
          ) : null}
          {reading && 'facts' in reading ? (
            <div className="dc-github-branch-result" data-status={reading.status}>
              <p className="dc-github-branch-state">{BRANCH_OBSERVATION_STATEMENTS[reading.status]}</p>
              <dl className="dc-github-branch-observation">
                {'ref' in reading ? (
                  <>
                    <dt>GitHub branch</dt>
                    <dd>{reading.ref.branchName}</dd>
                    <dt>GitHub commit</dt>
                    <dd>{reading.ref.commitSha}</dd>
                  </>
                ) : null}
                <dt>Repository ID</dt>
                <dd>{reading.facts.repositoryNodeId}</dd>
                <dt>Owner</dt>
                <dd>{reading.facts.owner}</dd>
                <dt>Repository</dt>
                <dd>{reading.facts.repository}</dd>
                <dt>Observed (UTC)</dt>
                <dd>{toUtcText(reading.facts.observedAtUtc)}</dd>
              </dl>
            </div>
          ) : null}
        </div>
      )}
    </section>
  )
}
