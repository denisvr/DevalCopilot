import { useMemo } from 'react'
import { CHECK_RUN_OUTPUT_STATEMENTS } from '../gitHubCheckRunOutputObservation'
import type { CheckRunOutputText } from '../gitHubCheckRunOutputObservation'
import type { ObservationScope, ObservationSource } from '../gitHubObservationTarget'
import { useGitHubCheckRunOutputObservation } from '../hooks/useGitHubCheckRunOutputObservation'
import { toUtcText } from '../utcText'

const CHECK_NOTE =
  'Check queries GitHub (github.com) through the GitHub CLI, using its default stored authentication, in two steps: first only whether this check run belongs to exactly the recorded owner and repository, the repository identity bound for this target and the recorded commit, and only then its name, status, conclusion, title, summary and text. The host reads that binding itself on every check; nothing another region showed is reused. A reading is only what GitHub returned at that moment and says nothing about the future. Nothing is checked until you ask, and nothing is repeated automatically.'

const FAILURES = {
  read: 'The GitHub check output could not be read.',
  invalid: 'The host answered with a result that is not coherent for this recorded target and check run, so none of it is shown.',
} as const

interface GitHubCheckRunOutputPanelProps {
  /** The exact completed local delivery of the recorded target. */
  source: ObservationSource
  /** The immutable recorded target, already proven coherent for `source`. */
  target: ObservationScope
  /** The opaque ID of the one check run whose output is inspected, taken from an admitted check-run entry. */
  checkRunId: string
  /** The parent contexts reading this region belongs to; a new reading ends this region's ownership. */
  parent: object
}

type Field = 'title' | 'summary' | 'text'
const FIELDS: readonly { key: Field; label: string }[] = [
  { key: 'title', label: 'Title' },
  { key: 'summary', label: 'Summary' },
  { key: 'text', label: 'Text' },
]

function stateOf(value: string | null): 'null' | 'empty' | 'whitespace' | 'text' {
  if (value === null) {
    return 'null'
  }

  if (value.length === 0) {
    return 'empty'
  }

  return value.trim().length === 0 ? 'whitespace' : 'text'
}

const DISCLOSURES = {
  null: CHECK_RUN_OUTPUT_STATEMENTS.NullField,
  empty: CHECK_RUN_OUTPUT_STATEMENTS.EmptyField,
  whitespace: CHECK_RUN_OUTPUT_STATEMENTS.WhitespaceField,
} as const

/** One returned value as inert text: a fixed disclosure for null, empty and whitespace-only values, and plain preformatted text for any value that is not null or empty. */
function OutputField({ label, field, value }: { label: string; field: Field; value: string | null }) {
  const state = stateOf(value)
  return (
    <div className="dc-github-output-field" data-state={state}>
      <h6>{label}</h6>
      {state === 'null' || state === 'empty' ? <p className="dc-github-output-note">{DISCLOSURES[state]}</p> : null}
      {state === 'whitespace' ? <p className="dc-github-output-note">{DISCLOSURES.whitespace}</p> : null}
      {state === 'text' || state === 'whitespace' ? (
        <pre className="dc-github-output-text" data-field={field}>
          {value}
        </pre>
      ) : null}
    </div>
  )
}

function OutputFields({ output }: { output: CheckRunOutputText }) {
  return (
    <>
      {FIELDS.map(({ key, label }) => (
        <OutputField key={key} label={label} field={key} value={output[key]} />
      ))}
    </>
  )
}

/**
 * Explicit, collapsed output region of one check run entry of a ContextsReported GitHub check details reading (ADR-0047), separate from
 * the contexts region's own state. It reads only on the explicit check or recheck, shows the exact recorded source and scope before
 * anything is checked, and renders the answer as inert, bounded plain text: the bound repository ID, the returned branch and commit, the
 * host's observation time, the check's literal status and conclusion and its title, summary and text in preformatted blocks, never
 * Markdown or HTML, never a link, copy, download, Agent or other action, never an aggregate verdict and never a translated word such as
 * passed or ready. A different commit, a ref not returned, an unavailable check, an identity change and any drift expose no output. The
 * fixed disclosure states that the text is untrusted provider text, redacted for five supported secret shapes only. Nothing is read on
 * mount, opening, a project, run, target, check or parent change, a reconnect or a timer. A new parent reading ends this region.
 */
export function GitHubCheckRunOutputPanel({ source, target, checkRunId, parent }: GitHubCheckRunOutputPanelProps) {
  const scope = useMemo(() => ({ source, target, checkRunId }), [source, target, checkRunId])
  const { open, phase, show, close, check } = useGitHubCheckRunOutputObservation(scope, parent)
  const reading = phase.kind === 'result' ? phase.reading : null
  // Only the opaque identifier names the region: provider text never reaches an attribute.
  const label = `GitHub check output for check run ${checkRunId}`

  return (
    <section className="dc-github-output" aria-label={label}>
      {!open ? (
        <button type="button" onClick={show}>
          Show GitHub check output
        </button>
      ) : (
        <div className="dc-github-output-body">
          <p className="dc-github-output-note">{CHECK_NOTE}</p>
          <p className="dc-github-output-note">{CHECK_RUN_OUTPUT_STATEMENTS.Disclosure}</p>
          <p className="dc-github-output-note">{CHECK_RUN_OUTPUT_STATEMENTS.Redaction}</p>
          <dl className="dc-github-output-scope">
            <dt>Recorded branch</dt>
            <dd>{source.branchName}</dd>
            <dt>Recorded local commit</dt>
            <dd>{source.commitSha}</dd>
            <dt>Recorded owner</dt>
            <dd>{target.owner}</dd>
            <dt>Recorded repository</dt>
            <dd>{target.repository}</dd>
            <dt>Check run ID</dt>
            <dd>{checkRunId}</dd>
          </dl>
          <p>
            <button type="button" onClick={check} disabled={phase.kind === 'pending'}>
              {phase.kind === 'idle' ? 'Check GitHub check output' : 'Recheck GitHub check output'}
            </button>{' '}
            <button type="button" onClick={close}>
              Close GitHub check output
            </button>
          </p>
          {phase.kind === 'idle' ? <p className="dc-github-output-state">Not checked.</p> : null}
          {phase.kind === 'pending' ? <p className="dc-github-output-state">Querying GitHub for the check output…</p> : null}
          {phase.kind === 'failed' ? (
            <p className="dc-github-output-state" role="alert">
              {FAILURES[phase.reason]}
            </p>
          ) : null}
          {reading && !('facts' in reading) ? (
            <div className="dc-github-output-outcome">
              <p className="dc-github-output-state" data-status={reading.status}>
                {CHECK_RUN_OUTPUT_STATEMENTS[reading.status]}
              </p>
              {'binding' in reading ? (
                <dl className="dc-github-output-bound">
                  <dt>Bound repository ID</dt>
                  <dd>{reading.binding.repositoryNodeId}</dd>
                </dl>
              ) : null}
            </div>
          ) : null}
          {reading && 'facts' in reading ? (
            <div className="dc-github-output-result" data-status={reading.status}>
              <p className="dc-github-output-state">{CHECK_RUN_OUTPUT_STATEMENTS[reading.status]}</p>
              <dl className="dc-github-output-observation">
                {'ref' in reading ? (
                  <>
                    <dt>GitHub branch</dt>
                    <dd>{reading.ref.branchName}</dd>
                    <dt>GitHub commit</dt>
                    <dd>{reading.ref.commitSha}</dd>
                  </>
                ) : null}
                {'checkRun' in reading ? (
                  <>
                    <dt>Check name</dt>
                    <dd>{reading.checkRun.name}</dd>
                    <dt>Check run ID</dt>
                    <dd>{reading.checkRun.id}</dd>
                    <dt>Check suite ID</dt>
                    <dd>{reading.checkRun.checkSuiteId}</dd>
                    <dt>Check suite commit</dt>
                    <dd>{reading.checkRun.commitSha}</dd>
                    <dt>Status</dt>
                    <dd>{reading.checkRun.status}</dd>
                    <dt>Conclusion</dt>
                    <dd>{reading.checkRun.conclusion ?? 'not returned'}</dd>
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
              {reading.status === 'OutputReported' ? <OutputFields output={reading.output} /> : null}
            </div>
          ) : null}
        </div>
      )}
    </section>
  )
}
