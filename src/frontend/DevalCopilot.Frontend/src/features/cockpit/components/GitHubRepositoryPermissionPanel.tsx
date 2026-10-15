import { useMemo } from 'react'
import { PERMISSION_OBSERVATION_STATEMENTS } from '../gitHubRepositoryPermissionObservation'
import type { ObservationScope, ObservationSource } from '../gitHubObservationTarget'
import { useGitHubRepositoryPermissionObservation } from '../hooks/useGitHubRepositoryPermissionObservation'
import { toUtcText } from '../utcText'

const CHECK_NOTE =
  'Check queries GitHub (github.com) through the GitHub CLI, using its default stored authentication, for exactly the recorded owner and repository and the repository identity bound for this target. The host reads that binding itself on every check; nothing another region showed is reused. Only the repository ID, its names and the repository permission GitHub reports for that authentication are read. A reading is only what GitHub reported at that moment and says nothing about the future. Nothing is checked until you ask, and nothing is repeated automatically.'

const FAILURES = {
  read: 'The GitHub repository permission could not be read.',
  invalid: 'The host answered with a result that is not coherent for this recorded target, so none of it is shown.',
} as const

interface GitHubRepositoryPermissionPanelProps {
  /** The exact completed local delivery of the recorded target. */
  source: ObservationSource
  /** The immutable recorded target, already proven coherent for `source`. */
  target: ObservationScope
}

/**
 * Explicit, collapsed repository permission region beside an Available GitHub observation target (ADR-0049), separate from the identity,
 * remote branch, identity binding, commit checks and check details regions. It reads only on the explicit check or recheck, shows the exact
 * recorded scope before anything is checked, and renders the answer as inert plain text: the bound repository ID, the exact literal role
 * GitHub reported (or that none was returned), the returned identity and the host's observation time, never a link, copy, execution or
 * other action, never a derived word such as allowed, denied or ready, and no approval or publication control depends on it. Nothing is read
 * on mount, opening, a project, run or target change, a reconnect or a timer.
 */
export function GitHubRepositoryPermissionPanel({ source, target }: GitHubRepositoryPermissionPanelProps) {
  const scope = useMemo(() => ({ source, target }), [source, target])
  const { open, phase, show, close, check } = useGitHubRepositoryPermissionObservation(scope)
  const reading = phase.kind === 'result' ? phase.reading : null

  return (
    <section className="dc-github-permission" aria-label="GitHub repository permission">
      {!open ? (
        <button type="button" onClick={show}>
          Show GitHub repository permission
        </button>
      ) : (
        <div className="dc-github-permission-body">
          <h5>GitHub repository permission</h5>
          <p className="dc-github-permission-note">{CHECK_NOTE}</p>
          <p className="dc-github-permission-note">{PERMISSION_OBSERVATION_STATEMENTS.Disclosure}</p>
          <dl className="dc-github-permission-scope">
            <dt>Recorded owner</dt>
            <dd>{target.owner}</dd>
            <dt>Recorded repository</dt>
            <dd>{target.repository}</dd>
          </dl>
          <p>
            <button type="button" onClick={check} disabled={phase.kind === 'pending'}>
              {phase.kind === 'idle' ? 'Check GitHub repository permission' : 'Recheck GitHub repository permission'}
            </button>{' '}
            <button type="button" onClick={close}>
              Close GitHub repository permission
            </button>
          </p>
          {phase.kind === 'idle' ? <p className="dc-github-permission-state">Not checked.</p> : null}
          {phase.kind === 'pending' ? <p className="dc-github-permission-state">Querying GitHub for the repository permission…</p> : null}
          {phase.kind === 'failed' ? (
            <p className="dc-github-permission-state" role="alert">
              {FAILURES[phase.reason]}
            </p>
          ) : null}
          {reading && !('facts' in reading) ? (
            <div className="dc-github-permission-outcome">
              <p className="dc-github-permission-state" data-status={reading.status}>
                {PERMISSION_OBSERVATION_STATEMENTS[reading.status]}
              </p>
              {'binding' in reading ? (
                <dl className="dc-github-permission-bound">
                  <dt>Bound repository ID</dt>
                  <dd>{reading.binding.repositoryNodeId}</dd>
                </dl>
              ) : null}
            </div>
          ) : null}
          {reading && 'facts' in reading ? (
            <div className="dc-github-permission-result" data-status={reading.status}>
              <p className="dc-github-permission-state">{PERMISSION_OBSERVATION_STATEMENTS[reading.status]}</p>
              <dl className="dc-github-permission-observation">
                {'viewerPermission' in reading ? (
                  <>
                    <dt>GitHub repository permission</dt>
                    <dd>{reading.viewerPermission}</dd>
                  </>
                ) : null}
                <dt>Bound repository ID</dt>
                <dd>{reading.binding.repositoryNodeId}</dd>
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
