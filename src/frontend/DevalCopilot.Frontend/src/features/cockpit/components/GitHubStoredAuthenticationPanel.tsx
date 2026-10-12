import { STATUS_STATEMENTS } from '../gitHubStoredAuthentication'
import { useGitHubStoredAuthentication } from '../hooks/useGitHubStoredAuthentication'
import { toUtcText } from '../utcText'

const SCOPE_NOTE =
  'This checks only the default credentials the GitHub CLI has stored for github.com. A token supplied through the environment and a custom CLI configuration are not observed, so this is not your complete ambient CLI session. Authentication proves neither repository permission nor publication authority.'

const FAILURES = {
  read: 'The stored authentication could not be checked.',
  invalid: 'The host answered with a status that is not coherent, so none of it is shown.',
} as const

/**
 * Host-level, explicit observation of the default stored GitHub CLI authentication (ADR-0038). It is collapsed until the
 * owner opens it and reads only when the owner presses Check or Check again; nothing is read on mount, selection, reconnect
 * or a timer, and an answer belongs to the opening that asked for it. It changes no Agent readiness, claim eligibility,
 * budget, history or delivery behavior.
 */
export function GitHubStoredAuthenticationPanel() {
  const { open, phase, show, close, check } = useGitHubStoredAuthentication()

  return (
    <section className="dc-github-auth" aria-label="GitHub stored authentication">
      {!open ? (
        <button type="button" onClick={show}>
          Show GitHub stored authentication
        </button>
      ) : (
        <div className="dc-github-auth-body">
          <h2>GitHub stored authentication</h2>
          <p className="dc-github-auth-note">{SCOPE_NOTE}</p>
          <p>
            <button type="button" onClick={check} disabled={phase.kind === 'pending'}>
              {phase.kind === 'result' || phase.kind === 'failed' ? 'Check again' : 'Check stored authentication'}
            </button>{' '}
            <button type="button" onClick={close}>
              Close GitHub stored authentication
            </button>
          </p>
          {phase.kind === 'idle' ? <p className="dc-github-auth-state">Not checked.</p> : null}
          {phase.kind === 'pending' ? <p className="dc-github-auth-state">Checking stored authentication…</p> : null}
          {phase.kind === 'failed' ? (
            <p className="dc-github-auth-state" role="alert">
              {FAILURES[phase.reason]}
            </p>
          ) : null}
          {phase.kind === 'result' ? (
            <div className="dc-github-auth-result" data-status={phase.answer.status}>
              <p className="dc-github-auth-state">{STATUS_STATEMENTS[phase.answer.status]}</p>
              {phase.answer.observedAtUtc ? <p>Observed at {toUtcText(phase.answer.observedAtUtc)} (UTC).</p> : null}
            </div>
          ) : null}
        </div>
      )}
    </section>
  )
}
