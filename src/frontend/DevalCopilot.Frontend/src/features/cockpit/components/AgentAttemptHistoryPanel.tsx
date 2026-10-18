import type { AgentAttemptEvidenceResponse, AgentAttemptHistoryEntryResponse } from '../../../api/clients'
import { useAgentAttemptEvidence } from '../hooks/useAgentAttemptEvidence'
import { useAgentAttemptHistory } from '../hooks/useAgentAttemptHistory'
import type { AgentAttemptArtifactWindowTarget } from '../hooks/useAgentAttemptArtifactWindow'
import { AgentAttemptArtifactViewer } from './AgentAttemptArtifactViewer'
import { ClaudeTurnLimitFacts } from './ClaudeTurnLimitFacts'
import { DirectGuidanceFact } from './DirectGuidanceFact'
import { ModelContextLimitsFact } from './ModelContextLimitsFact'
import { CodexAccountUsageDecisionFact } from './CodexAccountUsageDecisionFact'
import { SelectedAttemptExecutionFacts } from './SelectedAttemptExecutionFacts'
import { ARTIFACT_PURPOSE_LABELS } from '../artifactPurposes'
import { describeRepairLineage } from '../describeRepairLineage'
import { toAgentAttemptIdentityExpectation } from '../agentAttemptHistory'
import type { AgentAttemptIdentityExpectation } from '../agentAttemptHistory'

const MESSAGES = {
  first: {
    read: 'The Agent attempt history could not be loaded.',
    invalid: 'The host answered with an Agent attempt history that is not coherent, so none of it is shown.',
  },
  older: {
    read: 'The older attempts could not be loaded. The attempts already shown are unchanged.',
    invalid: 'The host answered with older attempts that are not coherent, so none of that page is shown. The attempts already shown are unchanged.',
  },
} as const

function describeEntry(entry: AgentAttemptHistoryEntryResponse): string {
  if (entry.identityValid !== true) {
    return `Attempt #${entry.attemptNumber} · ${entry.status ?? 'status not readable'} · identity could not be verified`
  }

  const outcome = entry.outcome ? ` · Result: ${entry.outcome}` : ''
  const lineage = describeRepairLineage(entry.repairSourceAttemptId, entry.repairSourceAttemptNumber)
  return `Attempt #${entry.attemptNumber} · ${entry.role} · ${entry.provider} · ${entry.status}${outcome}${lineage ? ` · ${lineage}` : ''}`
}

interface AgentAttemptDetailProps {
  selectionKey: string
  runId: string
  /** What the evidence must match: the selected row's identity, number and immutable role, provider and response contract. */
  expected: AgentAttemptIdentityExpectation
  onClose: () => void
}

/**
 * The selected attempt's bounded evidence metadata, read when its selection lifetime begins, plus the on-demand sealed artifact viewer.
 * Everything shown is historical evidence about that past attempt — never the run's current state, a live tail, or proof of what a
 * provider account allows.
 */
function AgentAttemptDetail({ selectionKey, runId, expected, onClose }: AgentAttemptDetailProps) {
  const { attemptId } = expected
  const { state, retry } = useAgentAttemptEvidence({ selectionKey, runId, expected })

  return (
    <section className="dc-attempt-history-detail" aria-label="Selected Agent attempt evidence">
      <p>
        <button type="button" onClick={onClose}>
          Close attempt
        </button>
      </p>
      <p className="dc-card-evidence-caveat">
        Historical evidence for a past attempt; it says nothing about the run&apos;s current state.
      </p>
      {state.status === 'loading' && <p className="dc-empty-state">Loading attempt evidence…</p>}
      {state.status === 'not-found' && <p className="dc-empty-state">This attempt is no longer available.</p>}
      {state.status === 'identity-invalid' && (
        <p className="dc-empty-state" role="status">
          This attempt&apos;s recorded identity could not be verified, so its evidence is not shown.
        </p>
      )}
      {state.status === 'error' && (
        <p className="dc-card-evidence-error" role="status">
          {state.message}{' '}
          <button type="button" onClick={retry}>
            Retry
          </button>
        </p>
      )}
      {state.status === 'success' && (
        <AgentAttemptEvidenceBody target={{ selectionKey, runId, attemptId }} evidence={state.evidence} />
      )}
    </section>
  )
}

function AgentAttemptEvidenceBody({ target, evidence }: { target: AgentAttemptArtifactWindowTarget; evidence: AgentAttemptEvidenceResponse }) {
  const artifacts = evidence.artifacts ?? []
  const lineage = describeRepairLineage(evidence.repairSourceAttemptId, evidence.repairSourceAttemptNumber)

  return (
    <div>
      <p>
        Attempt #{evidence.attemptNumber} · {evidence.role} · {evidence.provider} · {evidence.attemptStatus}
        {evidence.outcome ? ` · Result: ${evidence.outcome}` : ''}
      </p>
      {lineage && <p className="dc-attempt-history-lineage">{lineage}. Provenance only.</p>}
      <SelectedAttemptExecutionFacts evidence={evidence} />
      {artifacts.length === 0 ? (
        <p className="dc-empty-state">No sealed artifacts were recorded for this attempt.</p>
      ) : (
        <ul className="dc-attempt-history-artifacts">
          {artifacts.map((artifact) => (
            <li key={artifact.purpose}>
              {ARTIFACT_PURPOSE_LABELS[artifact.purpose ?? ''] ?? artifact.purpose}: {artifact.byteLength} bytes
              {artifact.truncated === true ? ', capture truncated' : ''}
            </li>
          ))}
        </ul>
      )}
      <ClaudeTurnLimitFacts attemptFact={evidence.maxTurns} />
      <DirectGuidanceFact fact={evidence.directGuidance} />
      <ModelContextLimitsFact provider={evidence.provider} limits={evidence.modelContextLimits} />
      <CodexAccountUsageDecisionFact provider={evidence.provider} stop={evidence.accountUsageStop} decision={evidence.accountUsageDecision} />
      <AgentAttemptArtifactViewer target={target} artifacts={artifacts} />
    </div>
  )
}

/**
 * Read-only, on-demand history of one run's Agent attempts, newest first, with a single selected attempt drill-down. It serves the
 * current run in the usage rail and a selected historical run in Run history alike: nothing is fetched until the owner opens it, older
 * pages are explicit, evidence is read only for a selected attempt and raw artifact text only after a purpose is chosen and loaded.
 * Every page, selection, evidence read and text window belongs to the committed run and its open lifetime (see
 * `useAgentAttemptHistory`), so another run, a return to an earlier one, closing, reopening or unmounting shows none of the previous
 * ones. It mounts no mutation, live, eligibility or configuration control, and every value is rendered as text.
 */
export function AgentAttemptHistoryPanel({ runId }: { runId: string | null }) {
  const { open, setOpen, rows, pending, failure, failedPage, hasMore, loadOlder, retry, selected, selectionKey, select, closeSelected } =
    useAgentAttemptHistory(runId)
  const expected = selected === null ? null : toAgentAttemptIdentityExpectation(selected)

  return (
    <section className="dc-attempt-history" aria-label="Agent attempt history">
      <button type="button" aria-expanded={open} onClick={() => setOpen(!open)}>
        {open ? 'Hide Agent attempt history' : 'Show Agent attempt history'}
      </button>
      {open && runId !== null ? (
        <div className="dc-attempt-history-list">
          {pending === 'first' ? <p className="dc-empty-state">Loading attempt history…</p> : null}
          {pending === null && failure === null && rows.length === 0 ? (
            <p className="dc-empty-state">No Agent attempts have been recorded for this run.</p>
          ) : null}
          {rows.length > 0 ? (
            <ol className="dc-attempt-history-items">
              {rows.map((entry) => (
                <li key={entry.attemptId}>
                  <span>{describeEntry(entry)}</span>{' '}
                  {entry.identityValid === true && (
                    <button
                      type="button"
                      aria-label={`Inspect attempt ${entry.attemptNumber}`}
                      aria-pressed={selected?.attemptId === entry.attemptId}
                      onClick={() => select(entry.attemptId ?? '')}
                    >
                      Inspect
                    </button>
                  )}
                </li>
              ))}
            </ol>
          ) : null}
          {failure !== null && failedPage !== null ? (
            <p className="dc-card-evidence-error" role="status">
              {MESSAGES[failedPage][failure]}{' '}
              <button type="button" onClick={retry}>
                Retry
              </button>
            </p>
          ) : null}
          {pending === 'older' ? <p className="dc-empty-state">Loading older attempts…</p> : null}
          {hasMore ? (
            <button type="button" onClick={loadOlder}>
              Load older attempts
            </button>
          ) : null}
          {expected !== null && selectionKey !== null ? (
            <AgentAttemptDetail selectionKey={selectionKey} runId={runId} expected={expected} onClose={closeSelected} />
          ) : null}
        </div>
      ) : null}
    </section>
  )
}
