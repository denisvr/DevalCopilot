import { useMemo } from 'react'
import type { ObservationScope, ObservationSource } from '../gitHubObservationTarget'
import { BINDING_STATEMENTS } from '../gitHubRepositoryIdentityBinding'
import { REPOSITORY_OBSERVATION_STATEMENTS } from '../gitHubRepositoryObservation'
import { useGitHubRepositoryIdentityBinding } from '../hooks/useGitHubRepositoryIdentityBinding'
import { toUtcText } from '../utcText'

const CHECK_NOTE =
  'Load reads what is recorded here and contacts nothing. Check candidate queries GitHub (github.com) through the GitHub CLI, using its default stored authentication, for exactly the recorded owner and repository above and only reads the repository ID and names. Nothing is loaded, checked or bound until you ask, and nothing is repeated automatically.'

const LOAD_FAILURES = {
  read: 'The recorded repository identity binding could not be read.',
  invalid: 'The host answered with a result that is not coherent for this recorded target, so none of it is shown.',
} as const

const CANDIDATE_FAILURES = {
  read: 'The GitHub repository identity could not be read.',
  invalid: 'The host answered with a result that is not coherent for this recorded target, so none of it is shown.',
} as const

const WRITE_FAILURES = {
  refused:
    'The host refused this binding (the identity may have changed, a different identity may already be bound, or the recorded facts are not available). Nothing was changed by this request. Reload, then check the candidate again, before binding.',
  ambiguous:
    'The outcome of binding is unknown: the host may have recorded the identity. Reload to read what was recorded, then check the candidate again, before binding.',
} as const

interface GitHubRepositoryIdentityBindingPanelProps {
  /** The exact completed local delivery of the recorded target. */
  source: ObservationSource
  /** The immutable recorded target, already proven coherent for `source`. */
  target: ObservationScope
}

/**
 * Explicit, collapsed region beside an Available GitHub observation target for the immutable binding of its repository identity
 * (ADR-0043). It reads only on the explicit load or reload, observes a candidate only on the explicit check, and writes only through the
 * separate bind action, which is armed by a settled "not bound" read and an Observed candidate of this opening. It shows the exact
 * source, target, repository ID, names and a fixed immutable and historical disclosure before anything is bound, and renders every value
 * as inert, bounded text with UTC times: never a link, copy or other action. Recorded evidence is not a fresh check of GitHub.
 */
export function GitHubRepositoryIdentityBindingPanel({ source, target }: GitHubRepositoryIdentityBindingPanelProps) {
  const scope = useMemo(() => ({ source, target }), [source, target])
  const { open, load, candidate, write, candidateIdentity, canBind, show, close, reload, check, bind } =
    useGitHubRepositoryIdentityBinding(scope)
  const pending = load.kind === 'pending' || candidate.kind === 'pending' || write.kind === 'pending'
  const reading = load.kind === 'result' ? load.reading : null
  const candidateReading = candidate.kind === 'result' ? candidate.reading : null
  const bindable = reading?.kind === 'NotRecorded' && reading.bindable

  return (
    <section className="dc-github-binding" aria-label="GitHub identity binding">
      {!open ? (
        <button type="button" onClick={show}>
          Show GitHub identity binding
        </button>
      ) : (
        <div className="dc-github-binding-body">
          <h5>GitHub identity binding</h5>
          <p className="dc-github-binding-note">{CHECK_NOTE}</p>
          <dl className="dc-github-binding-scope">
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
            <button type="button" onClick={reload} disabled={pending}>
              {load.kind === 'idle' ? 'Load GitHub identity binding' : 'Reload GitHub identity binding'}
            </button>{' '}
            <button type="button" onClick={check} disabled={pending}>
              {candidate.kind === 'idle' ? 'Check candidate identity' : 'Recheck candidate identity'}
            </button>{' '}
            <button type="button" onClick={close}>
              Close GitHub identity binding
            </button>
          </p>
          {load.kind === 'idle' ? <p className="dc-github-binding-state">Not loaded.</p> : null}
          {load.kind === 'pending' ? <p className="dc-github-binding-state">Reading the recorded binding…</p> : null}
          {load.kind === 'failed' ? (
            <p className="dc-github-binding-state" role="alert">
              {LOAD_FAILURES[load.reason]}
            </p>
          ) : null}
          {reading?.kind === 'Unavailable' ? <p className="dc-github-binding-state">{BINDING_STATEMENTS.Unavailable}</p> : null}
          {reading?.kind === 'Available' ? (
            <div className="dc-github-binding-result" data-status="Available">
              <p className="dc-github-binding-state">{BINDING_STATEMENTS.Available}</p>
              <dl className="dc-github-binding-recorded">
                <dt>Repository ID</dt>
                <dd>{reading.binding.repositoryNodeId}</dd>
                <dt>Owner</dt>
                <dd>{reading.binding.owner}</dd>
                <dt>Repository</dt>
                <dd>{reading.binding.repository}</dd>
                <dt>Observed (UTC)</dt>
                <dd>{toUtcText(reading.binding.observedAtUtc)}</dd>
                <dt>Bound (UTC)</dt>
                <dd>{toUtcText(reading.binding.recordedAtUtc)}</dd>
                <dt>Binding event</dt>
                <dd>{reading.binding.eventId}</dd>
              </dl>
            </div>
          ) : null}
          {reading?.kind === 'NotRecorded' ? (
            <p className="dc-github-binding-state" data-status="NotRecorded">
              {reading.bindable ? BINDING_STATEMENTS.NotRecordedBindable : BINDING_STATEMENTS.NotRecordedNotBindable}
            </p>
          ) : null}
          {candidate.kind === 'pending' ? <p className="dc-github-binding-state">Querying GitHub for the candidate repository identity…</p> : null}
          {candidate.kind === 'failed' ? (
            <p className="dc-github-binding-state" role="alert">
              {CANDIDATE_FAILURES[candidate.reason]}
            </p>
          ) : null}
          {candidateReading && candidateReading.status !== 'Observed' ? (
            <p className="dc-github-binding-state" data-status={candidateReading.status}>
              {REPOSITORY_OBSERVATION_STATEMENTS[candidateReading.status]}
            </p>
          ) : null}
          {candidateIdentity ? (
            <div className="dc-github-binding-candidate" data-status="Observed">
              <p className="dc-github-binding-state">GitHub reported this repository identity at the time shown. It is only a candidate until you bind it:</p>
              <dl className="dc-github-binding-observation">
                <dt>Repository ID</dt>
                <dd>{candidateIdentity.repositoryNodeId}</dd>
                <dt>Owner</dt>
                <dd>{candidateIdentity.owner}</dd>
                <dt>Repository</dt>
                <dd>{candidateIdentity.repository}</dd>
                <dt>Observed (UTC)</dt>
                <dd>{toUtcText(candidateIdentity.observedAtUtc)}</dd>
              </dl>
            </div>
          ) : null}
          {bindable && candidateIdentity ? (
            <p className="dc-github-binding-review">
              Binding to record: github.com / {target.owner} / {target.repository} with repository ID {candidateIdentity.repositoryNodeId} for the local
              commit {source.commitSha} on the branch {source.branchName}. {BINDING_STATEMENTS.Disclosure}
            </p>
          ) : null}
          {bindable ? (
            <button type="button" onClick={bind} disabled={!canBind}>
              Bind this repository identity
            </button>
          ) : null}
          {write.kind === 'pending' ? <p className="dc-github-binding-state">Binding the repository identity…</p> : null}
          {write.kind === 'failed' ? (
            <p className="dc-github-binding-state" role="alert">
              {WRITE_FAILURES[write.reason]}
            </p>
          ) : null}
        </div>
      )}
    </section>
  )
}
