import { useMemo } from 'react'
import { CHECK_CONTEXTS_STATEMENTS } from '../gitHubCheckContextsObservation'
import type { CheckContextEntry } from '../gitHubCheckContextsObservation'
import type { ObservationScope, ObservationSource } from '../gitHubObservationTarget'
import { useGitHubCheckContextsObservation } from '../hooks/useGitHubCheckContextsObservation'
import { toUtcText } from '../utcText'

const CHECK_NOTE =
  'Check queries GitHub (github.com) through the GitHub CLI, using its default stored authentication, for exactly the recorded owner and repository, the recorded branch and the repository identity bound for this target. The host reads that binding itself on every check; nothing another region showed is reused. Only the repository ID, its names, the commit that branch points at and at most 50 of the checks and statuses GitHub lists for that commit are read: their kind, ID, name, status, conclusion and check suite ID. A reading is only what GitHub returned at that moment and says nothing about the future. Nothing is checked until you ask, and nothing is repeated automatically.'

const FAILURES = {
  read: 'The GitHub check details could not be read.',
  invalid: 'The host answered with a result that is not coherent for this recorded target, so none of it is shown.',
} as const

interface GitHubCheckContextsObservationPanelProps {
  /** The exact completed local delivery of the recorded target. */
  source: ObservationSource
  /** The immutable recorded target, already proven coherent for `source`. */
  target: ObservationScope
}

function Entry({ entry }: { entry: CheckContextEntry }) {
  return (
    <li data-kind={entry.kind}>
      <dl className="dc-github-contexts-entry">
        <dt>Kind</dt>
        <dd>{entry.kind}</dd>
        <dt>Name</dt>
        <dd>{entry.name}</dd>
        <dt>ID</dt>
        <dd>{entry.id}</dd>
        <dt>Status</dt>
        <dd>{entry.status}</dd>
        {entry.kind === 'CheckRun' ? (
          <>
            <dt>Conclusion</dt>
            <dd>{entry.conclusion ?? 'not returned'}</dd>
            <dt>Check suite ID</dt>
            <dd>{entry.checkSuiteId}</dd>
          </>
        ) : null}
      </dl>
    </li>
  )
}

/**
 * Explicit, collapsed GitHub check details region beside an Available GitHub observation target (ADR-0045), separate from the identity,
 * remote branch, identity binding and commit checks regions. It reads only on the explicit check or recheck, shows the exact local
 * source and recorded scope before anything is checked, and renders the answer as inert, bounded text: the bound repository ID, the
 * returned branch and commit, the host's observation time and each returned entry's literal kind, ID, name, status, conclusion and check
 * suite, never a link, copy or other action, never an aggregate verdict and never a translated word such as passed or ready. A different
 * commit, a ref not returned, an identity change, an exceeded bound and any drift expose no entry. Nothing is read on mount, opening, a
 * project, run or target change, a reconnect or a timer.
 */
export function GitHubCheckContextsObservationPanel({ source, target }: GitHubCheckContextsObservationPanelProps) {
  const scope = useMemo(() => ({ source, target }), [source, target])
  const { open, phase, show, close, check } = useGitHubCheckContextsObservation(scope)
  const reading = phase.kind === 'result' ? phase.reading : null

  return (
    <section className="dc-github-contexts" aria-label="GitHub check details">
      {!open ? (
        <button type="button" onClick={show}>
          Show GitHub check details
        </button>
      ) : (
        <div className="dc-github-contexts-body">
          <h5>GitHub check details</h5>
          <p className="dc-github-contexts-note">{CHECK_NOTE}</p>
          <p className="dc-github-contexts-note">{CHECK_CONTEXTS_STATEMENTS.Disclosure}</p>
          <p className="dc-github-contexts-note">{CHECK_CONTEXTS_STATEMENTS.LabelRedaction}</p>
          <dl className="dc-github-contexts-scope">
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
              {phase.kind === 'idle' ? 'Check GitHub check details' : 'Recheck GitHub check details'}
            </button>{' '}
            <button type="button" onClick={close}>
              Close GitHub check details
            </button>
          </p>
          {phase.kind === 'idle' ? <p className="dc-github-contexts-state">Not checked.</p> : null}
          {phase.kind === 'pending' ? <p className="dc-github-contexts-state">Querying GitHub for the check details…</p> : null}
          {phase.kind === 'failed' ? (
            <p className="dc-github-contexts-state" role="alert">
              {FAILURES[phase.reason]}
            </p>
          ) : null}
          {reading && !('facts' in reading) ? (
            <div className="dc-github-contexts-outcome">
              <p className="dc-github-contexts-state" data-status={reading.status}>
                {CHECK_CONTEXTS_STATEMENTS[reading.status]}
              </p>
              {'binding' in reading ? (
                <dl className="dc-github-contexts-bound">
                  <dt>Bound repository ID</dt>
                  <dd>{reading.binding.repositoryNodeId}</dd>
                </dl>
              ) : null}
            </div>
          ) : null}
          {reading && 'facts' in reading ? (
            <div className="dc-github-contexts-result" data-status={reading.status}>
              <p className="dc-github-contexts-state">
                {reading.status === 'ContextsReported' && reading.totalCount === 0
                  ? CHECK_CONTEXTS_STATEMENTS.ZeroReturned
                  : CHECK_CONTEXTS_STATEMENTS[reading.status]}
              </p>
              <dl className="dc-github-contexts-observation">
                {'ref' in reading ? (
                  <>
                    <dt>GitHub branch</dt>
                    <dd>{reading.ref.branchName}</dd>
                    <dt>GitHub commit</dt>
                    <dd>{reading.ref.commitSha}</dd>
                  </>
                ) : null}
                {reading.status === 'LimitExceeded' ? (
                  <>
                    <dt>Total reported by GitHub</dt>
                    <dd>{reading.totalCount}</dd>
                  </>
                ) : null}
                {reading.status === 'ContextsReported' ? (
                  <>
                    <dt>Returned entries</dt>
                    <dd>{reading.totalCount}</dd>
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
              {reading.status === 'ContextsReported' && reading.contexts.length > 0 ? (
                <ol className="dc-github-contexts-entries">
                  {reading.contexts.map((entry) => (
                    <Entry key={entry.id} entry={entry} />
                  ))}
                </ol>
              ) : null}
            </div>
          ) : null}
        </div>
      )}
    </section>
  )
}
