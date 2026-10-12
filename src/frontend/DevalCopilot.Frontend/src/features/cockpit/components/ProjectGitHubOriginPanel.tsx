import { ORIGIN_STATEMENTS } from '../projectGitHubOrigin'
import { useProjectGitHubOrigin } from '../hooks/useProjectGitHubOrigin'
import { toUtcText } from '../utcText'

const SCOPE_NOTE =
  "This reads only the origin URL stored in this repository's own local Git configuration. Global, system, included and worktree settings, URL rewrite rules and a separate push destination are ignored, so Git itself may use a different address. Nothing contacts GitHub: this is not a verified remote identity, permission, CI result or publication authority."

const FAILURES = {
  read: 'The project GitHub origin could not be inspected.',
  invalid: 'The host answered with a result that is not coherent for this project, so none of it is shown.',
} as const

interface ProjectGitHubOriginPanelProps {
  /** The selected project; null when none is selected. */
  projectId: string | null
}

/**
 * Project-level, explicit observation of the repository-local GitHub origin configuration (ADR-0039). It is collapsed for every
 * project until the owner opens it and reads only when the owner presses Inspect or Inspect again; nothing is read on mount,
 * selection, reconnect or a timer, and an answer belongs to the opening of the project that asked for it. The target is plain
 * text; nothing here links, copies, opens or changes anything, and it affects no Agent readiness, claim, budget, history or
 * delivery behavior.
 */
export function ProjectGitHubOriginPanel({ projectId }: ProjectGitHubOriginPanelProps) {
  const { open, phase, show, close, check } = useProjectGitHubOrigin(projectId)

  return (
    <section className="dc-github-origin" aria-label="Project GitHub origin">
      {!open ? (
        <button type="button" onClick={show} disabled={projectId === null}>
          Show project GitHub origin
        </button>
      ) : (
        <div className="dc-github-origin-body">
          <h2>Project GitHub origin</h2>
          <p className="dc-github-origin-note">{SCOPE_NOTE}</p>
          <p>
            <button type="button" onClick={check} disabled={phase.kind === 'pending'}>
              {phase.kind === 'result' || phase.kind === 'failed' ? 'Inspect again' : 'Inspect GitHub origin'}
            </button>{' '}
            <button type="button" onClick={close}>
              Close project GitHub origin
            </button>
          </p>
          {phase.kind === 'idle' ? <p className="dc-github-origin-state">Not inspected.</p> : null}
          {phase.kind === 'pending' ? <p className="dc-github-origin-state">Inspecting the local Git configuration…</p> : null}
          {phase.kind === 'failed' ? (
            <p className="dc-github-origin-state" role="alert">
              {FAILURES[phase.reason]}
            </p>
          ) : null}
          {phase.kind === 'result' ? (
            <div className="dc-github-origin-result" data-status={phase.answer.status}>
              {phase.answer.status === 'Available' && phase.answer.target ? (
                <>
                  <p className="dc-github-origin-state">A GitHub origin is configured in the local Git configuration:</p>
                  <dl className="dc-github-origin-target">
                    <dt>Host</dt>
                    <dd>github.com</dd>
                    <dt>Owner</dt>
                    <dd>{phase.answer.target.owner}</dd>
                    <dt>Repository</dt>
                    <dd>{phase.answer.target.repository}</dd>
                  </dl>
                </>
              ) : (
                <p className="dc-github-origin-state">{ORIGIN_STATEMENTS[phase.answer.status === 'NotConfigured' ? 'NotConfigured' : 'Unavailable']}</p>
              )}
              {phase.answer.observedAtUtc ? <p>Observed at {toUtcText(phase.answer.observedAtUtc)} (UTC).</p> : null}
            </div>
          ) : null}
        </div>
      )}
    </section>
  )
}
