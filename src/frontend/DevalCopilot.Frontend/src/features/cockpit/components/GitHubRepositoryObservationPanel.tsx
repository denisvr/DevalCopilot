import { useMemo } from 'react'
import type { ObservationScope, ObservationSource } from '../gitHubObservationTarget'
import { REPOSITORY_OBSERVATION_STATEMENTS } from '../gitHubRepositoryObservation'
import { useGitHubRepositoryObservation } from '../hooks/useGitHubRepositoryObservation'
import { toUtcText } from '../utcText'

const CHECK_NOTE =
  'Check queries GitHub (github.com) through the GitHub CLI, using its default stored authentication, for exactly the recorded owner and repository above. Only the repository ID and its names are read. It is not proof of a remote branch or commit, a permission, a CI result or publication approval, and it does not bind or change anything: nothing is checked until you ask, and nothing is repeated automatically.'

const FAILURES = {
  read: 'The GitHub repository identity could not be read.',
  invalid: 'The host answered with a result that is not coherent for this recorded target, so none of it is shown.',
} as const

interface GitHubRepositoryObservationPanelProps {
  /** The exact completed local delivery of the recorded target. */
  source: ObservationSource
  /** The immutable recorded target, already proven coherent for `source`. */
  target: ObservationScope
}

/**
 * Explicit, collapsed identity region beside an Available GitHub observation target (ADR-0041). It reads only on the explicit check or
 * recheck, shows the exact source and recorded target before anything is checked, and renders the answer as inert, bounded text: the
 * repository ID and names GitHub returned and the host's observation time, never a link, copy or other action. Nothing is read on
 * mount, opening, a project, run or target change, a reconnect or a timer.
 */
export function GitHubRepositoryObservationPanel({ source, target }: GitHubRepositoryObservationPanelProps) {
  const scope = useMemo(() => ({ source, target }), [source, target])
  const { open, phase, show, close, check } = useGitHubRepositoryObservation(scope)
  const reading = phase.kind === 'result' ? phase.reading : null

  return (
    <section className="dc-github-identity" aria-label="GitHub repository identity">
      {!open ? (
        <button type="button" onClick={show}>
          Show GitHub repository identity
        </button>
      ) : (
        <div className="dc-github-identity-body">
          <h5>GitHub repository identity</h5>
          <p className="dc-github-identity-note">{CHECK_NOTE}</p>
          <dl className="dc-github-identity-scope">
            <dt>Branch</dt>
            <dd>{source.branchName}</dd>
            <dt>Local commit</dt>
            <dd>{source.commitSha}</dd>
            <dt>Recorded owner</dt>
            <dd>{target.owner}</dd>
            <dt>Recorded repository</dt>
            <dd>{target.repository}</dd>
          </dl>
          <p>
            <button type="button" onClick={check} disabled={phase.kind === 'pending'}>
              {phase.kind === 'idle' ? 'Check GitHub repository identity' : 'Recheck GitHub repository identity'}
            </button>{' '}
            <button type="button" onClick={close}>
              Close GitHub repository identity
            </button>
          </p>
          {phase.kind === 'idle' ? <p className="dc-github-identity-state">Not checked.</p> : null}
          {phase.kind === 'pending' ? <p className="dc-github-identity-state">Querying GitHub for the repository identity…</p> : null}
          {phase.kind === 'failed' ? (
            <p className="dc-github-identity-state" role="alert">
              {FAILURES[phase.reason]}
            </p>
          ) : null}
          {reading && reading.status !== 'Observed' ? (
            <p className="dc-github-identity-state" data-status={reading.status}>
              {REPOSITORY_OBSERVATION_STATEMENTS[reading.status]}
            </p>
          ) : null}
          {reading?.status === 'Observed' ? (
            <div className="dc-github-identity-result" data-status="Observed">
              <p className="dc-github-identity-state">{REPOSITORY_OBSERVATION_STATEMENTS.Observed}</p>
              <dl className="dc-github-identity-observation">
                <dt>Repository ID</dt>
                <dd>{reading.identity.repositoryNodeId}</dd>
                <dt>Owner</dt>
                <dd>{reading.identity.owner}</dd>
                <dt>Repository</dt>
                <dd>{reading.identity.repository}</dd>
                <dt>Observed (UTC)</dt>
                <dd>{toUtcText(reading.identity.observedAtUtc)}</dd>
              </dl>
            </div>
          ) : null}
        </div>
      )}
    </section>
  )
}
