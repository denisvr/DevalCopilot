import { OBSERVATION_STATEMENTS } from '../gitHubObservationTarget'
import type { ObservationSource } from '../gitHubObservationTarget'
import { useGitHubObservationTarget } from '../hooks/useGitHubObservationTarget'
import { isGitHubOwner, isGitHubRepository } from '../gitHubRepositoryNames'
import { toUtcText } from '../utcText'
import { GitHubRepositoryObservationPanel } from './GitHubRepositoryObservationPanel'
import { GitHubBranchObservationPanel } from './GitHubBranchObservationPanel'
import { GitHubRepositoryIdentityBindingPanel } from './GitHubRepositoryIdentityBindingPanel'
import { GitHubCiObservationPanel } from './GitHubCiObservationPanel'

const SCOPE_NOTE =
  'Recording is your own local selection of which GitHub repository (on github.com) is later to be observed for this exact local commit. It is not a verified remote identity, a permission, a CI result or publication authority: nothing contacts GitHub, nothing is pushed, and nothing is observed automatically. Once recorded it cannot be changed, replaced or removed by this operation.'
const NO_SOURCE = 'This local delivery does not carry the facts needed to record a GitHub observation target, so none can be recorded.'

const FAILURES = {
  read: 'The GitHub observation target could not be read.',
  invalid: 'The host answered with a result that is not coherent for this local delivery, so none of it is shown.',
} as const

const WRITE_FAILURES = {
  refused:
    'The host refused this selection (a different selection may already be recorded, or this delivery is not available as a source). Nothing was changed by this request. Reload to read what is recorded before recording again.',
  ambiguous:
    'The outcome of recording is unknown: the host may have recorded the selection. Reload to read what was recorded before recording again.',
} as const

interface GitHubObservationTargetPanelProps {
  /** The completed local delivery this selection is for; null when the operation lacks any identifying fact. */
  source: ObservationSource | null
}

/**
 * Explicit, collapsed panel beside the completed local commit for the immutable Human selection of its GitHub observation scope
 * (ADR-0040). It reads only when the owner loads or reloads and writes only through the separate record action, with blank owner and
 * repository fields: nothing is defaulted from an origin or an authentication observation, probed or recorded automatically. The
 * exact branch and commit and the selection that will be recorded are shown before submission, together with a fixed disclosure that
 * the selection is local only. Every value is rendered as text; nothing here links, copies or opens anything.
 */
export function GitHubObservationTargetPanel({ source }: GitHubObservationTargetPanelProps) {
  const { open, load, write, owner, repository, canRecord, show, close, reload, setOwner, setRepository, record } =
    useGitHubObservationTarget(source)
  const pending = load.kind === 'pending' || write.kind === 'pending'
  const reading = load.kind === 'result' ? load.reading : null
  const draftValid = isGitHubOwner(owner) && isGitHubRepository(repository)

  return (
    <section className="dc-github-target" aria-label="GitHub observation target">
      {!open ? (
        <button type="button" onClick={show} disabled={source === null}>
          Show GitHub observation target
        </button>
      ) : (
        <div className="dc-github-target-body">
          <h4>GitHub observation target</h4>
          <p className="dc-github-target-note">{SCOPE_NOTE}</p>
          {source === null ? (
            <p className="dc-github-target-state">{NO_SOURCE}</p>
          ) : (
            <dl className="dc-github-target-source">
              <dt>Branch</dt>
              <dd>{source.branchName}</dd>
              <dt>Local commit</dt>
              <dd>{source.commitSha}</dd>
            </dl>
          )}
          <p>
            <button type="button" onClick={reload} disabled={pending || source === null}>
              {load.kind === 'idle' ? 'Load GitHub observation target' : 'Reload GitHub observation target'}
            </button>{' '}
            <button type="button" onClick={close}>
              Close GitHub observation target
            </button>
          </p>
          {load.kind === 'idle' ? <p className="dc-github-target-state">Not loaded.</p> : null}
          {load.kind === 'pending' ? <p className="dc-github-target-state">Reading the recorded GitHub observation target…</p> : null}
          {load.kind === 'failed' ? (
            <p className="dc-github-target-state" role="alert">
              {FAILURES[load.reason]}
            </p>
          ) : null}
          {reading?.kind === 'Unavailable' ? <p className="dc-github-target-state">{OBSERVATION_STATEMENTS.Unavailable}</p> : null}
          {reading?.kind === 'Available' ? (
            <div className="dc-github-target-result" data-status="Available">
              <p className="dc-github-target-state">This GitHub observation target is recorded for this local delivery and is immutable:</p>
              <dl className="dc-github-target-selection">
                <dt>Host</dt>
                <dd>{reading.target.host}</dd>
                <dt>Owner</dt>
                <dd>{reading.target.owner}</dd>
                <dt>Repository</dt>
                <dd>{reading.target.repository}</dd>
                <dt>Recorded (UTC)</dt>
                <dd>{toUtcText(reading.target.recordedAtUtc)}</dd>
                <dt>Recording event</dt>
                <dd>{reading.target.eventId}</dd>
              </dl>
              {source ? <GitHubRepositoryObservationPanel source={source} target={reading.target} /> : null}
              {source ? <GitHubBranchObservationPanel source={source} target={reading.target} /> : null}
              {source ? <GitHubRepositoryIdentityBindingPanel source={source} target={reading.target} /> : null}
              {source ? <GitHubCiObservationPanel source={source} target={reading.target} /> : null}
            </div>
          ) : null}
          {reading?.kind === 'NotRecorded' ? (
            <div className="dc-github-target-result" data-status="NotRecorded">
              <p className="dc-github-target-state">
                {reading.recordable ? OBSERVATION_STATEMENTS.NotRecordedRecordable : OBSERVATION_STATEMENTS.NotRecordedNotRecordable}
              </p>
              {reading.recordable ? (
                <>
                  <label>
                    GitHub owner
                    <input
                      type="text"
                      value={owner}
                      disabled={write.kind === 'pending' || write.kind === 'failed'}
                      autoComplete="off"
                      spellCheck={false}
                      aria-invalid={owner !== '' && !isGitHubOwner(owner) ? true : undefined}
                      onChange={(event) => setOwner(event.target.value)}
                    />
                  </label>
                  <label>
                    GitHub repository
                    <input
                      type="text"
                      value={repository}
                      disabled={write.kind === 'pending' || write.kind === 'failed'}
                      autoComplete="off"
                      spellCheck={false}
                      aria-invalid={repository !== '' && !isGitHubRepository(repository) ? true : undefined}
                      onChange={(event) => setRepository(event.target.value)}
                    />
                  </label>
                  {(owner !== '' || repository !== '') && !draftValid ? (
                    <p className="dc-github-target-state">
                      The owner needs 1 to 39 letters, digits and hyphens (not at the start or end), and the repository 1 to 100 letters, digits, dots,
                      underscores and hyphens (not at the start). Nothing is trimmed or converted.
                    </p>
                  ) : null}
                  {draftValid && source ? (
                    <p className="dc-github-target-review">
                      Selection to record: github.com / {owner} / {repository} for the local commit {source.commitSha} on the branch{' '}
                      {source.branchName}. It cannot be changed after it is recorded.
                    </p>
                  ) : null}
                  <button type="button" onClick={record} disabled={!canRecord}>
                    Record GitHub observation target
                  </button>
                </>
              ) : null}
            </div>
          ) : null}
          {write.kind === 'pending' ? <p className="dc-github-target-state">Recording the selection…</p> : null}
          {write.kind === 'failed' ? (
            <p className="dc-github-target-state" role="alert">
              {WRITE_FAILURES[write.reason]}
            </p>
          ) : null}
        </div>
      )}
    </section>
  )
}
