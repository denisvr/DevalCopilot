import { useMemo } from 'react'
import { CI_OBSERVATION_STATEMENTS } from '../gitHubCiObservation'
import type { ObservationScope, ObservationSource } from '../gitHubObservationTarget'
import { useGitHubCiObservation } from '../hooks/useGitHubCiObservation'
import { toUtcText } from '../utcText'

const CHECK_NOTE =
  'Check queries GitHub (github.com) through the GitHub CLI, using its default stored authentication, for exactly the recorded owner and repository, the recorded branch and the repository identity bound for this target. The host reads that binding itself on every check; nothing another region showed is reused. Only the repository ID, its names, the commit that branch points at and GitHub\'s single aggregate check state for that commit are read. A reading is only what GitHub reported at that moment and says nothing about the future. Nothing is checked until you ask, and nothing is repeated automatically.'

const FAILURES = {
  read: 'The GitHub commit checks could not be read.',
  invalid: 'The host answered with a result that is not coherent for this recorded target, so none of it is shown.',
} as const

interface GitHubCiObservationPanelProps {
  /** The exact completed local delivery of the recorded target. */
  source: ObservationSource
  /** The immutable recorded target, already proven coherent for `source`. */
  target: ObservationScope
}

/**
 * Explicit, collapsed commit checks region beside an Available GitHub observation target (ADR-0044), separate from the identity, remote
 * branch and identity binding regions. It reads only on the explicit check or recheck, shows the exact local source and recorded scope
 * before anything is checked, and renders the answer as inert, bounded text: the bound repository ID, the returned branch and commit, the
 * literal GitHub state and the host's observation time, never a link, copy or other action, and never a translated word such as passed or
 * ready. Nothing is read on mount, opening, a project, run or target change, a reconnect or a timer.
 */
export function GitHubCiObservationPanel({ source, target }: GitHubCiObservationPanelProps) {
  const scope = useMemo(() => ({ source, target }), [source, target])
  const { open, phase, show, close, check } = useGitHubCiObservation(scope)
  const reading = phase.kind === 'result' ? phase.reading : null

  return (
    <section className="dc-github-ci" aria-label="GitHub commit checks">
      {!open ? (
        <button type="button" onClick={show}>
          Show GitHub commit checks
        </button>
      ) : (
        <div className="dc-github-ci-body">
          <h5>GitHub commit checks</h5>
          <p className="dc-github-ci-note">{CHECK_NOTE}</p>
          <p className="dc-github-ci-note">{CI_OBSERVATION_STATEMENTS.Disclosure}</p>
          <dl className="dc-github-ci-scope">
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
              {phase.kind === 'idle' ? 'Check GitHub commit checks' : 'Recheck GitHub commit checks'}
            </button>{' '}
            <button type="button" onClick={close}>
              Close GitHub commit checks
            </button>
          </p>
          {phase.kind === 'idle' ? <p className="dc-github-ci-state">Not checked.</p> : null}
          {phase.kind === 'pending' ? <p className="dc-github-ci-state">Querying GitHub for the commit checks…</p> : null}
          {phase.kind === 'failed' ? (
            <p className="dc-github-ci-state" role="alert">
              {FAILURES[phase.reason]}
            </p>
          ) : null}
          {reading && !('facts' in reading) ? (
            <div className="dc-github-ci-outcome">
              <p className="dc-github-ci-state" data-status={reading.status}>
                {CI_OBSERVATION_STATEMENTS[reading.status]}
              </p>
              {'binding' in reading ? (
                <dl className="dc-github-ci-bound">
                  <dt>Bound repository ID</dt>
                  <dd>{reading.binding.repositoryNodeId}</dd>
                </dl>
              ) : null}
            </div>
          ) : null}
          {reading && 'facts' in reading ? (
            <div className="dc-github-ci-result" data-status={reading.status}>
              <p className="dc-github-ci-state">{CI_OBSERVATION_STATEMENTS[reading.status]}</p>
              <dl className="dc-github-ci-observation">
                {'rollupState' in reading ? (
                  <>
                    <dt>GitHub check state</dt>
                    <dd>{reading.rollupState}</dd>
                  </>
                ) : null}
                {'ref' in reading ? (
                  <>
                    <dt>GitHub branch</dt>
                    <dd>{reading.ref.branchName}</dd>
                    <dt>GitHub commit</dt>
                    <dd>{reading.ref.commitSha}</dd>
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
