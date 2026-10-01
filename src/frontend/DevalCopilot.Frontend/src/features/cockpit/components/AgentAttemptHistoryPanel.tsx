import { useState } from 'react'
import type { AgentAttemptEvidenceResponse, AgentAttemptHistoryEntryResponse } from '../../../api/clients'
import { useAgentAttemptEvidence } from '../hooks/useAgentAttemptEvidence'
import { useAgentAttemptHistory } from '../hooks/useAgentAttemptHistory'
import { AgentAttemptArtifactViewer } from './AgentAttemptArtifactViewer'
import { ClaudeTurnLimitFacts } from './ClaudeTurnLimitFacts'
import { DirectGuidanceFact } from './DirectGuidanceFact'
import { ARTIFACT_PURPOSE_LABELS } from '../artifactPurposes'
import { describeRepairLineage } from '../describeRepairLineage'

function formatUtc(value: Date | string | undefined): string | null {
  if (value === undefined) {
    return null
  }

  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? null : `${date.toISOString().slice(0, 19).replace('T', ' ')} UTC`
}

function describeEntry(entry: AgentAttemptHistoryEntryResponse): string {
  if (entry.identityValid !== true) {
    return `Attempt #${entry.attemptNumber} · ${entry.status} · identity could not be verified`
  }

  const outcome = entry.outcome ? ` · Result: ${entry.outcome}` : ''
  const lineage = describeRepairLineage(entry.repairSourceAttemptId, entry.repairSourceAttemptNumber)
  return `Attempt #${entry.attemptNumber} · ${entry.role} · ${entry.provider} · ${entry.status}${outcome}${lineage ? ` · ${lineage}` : ''}`
}

interface AgentAttemptDetailProps {
  runId: string
  attemptId: string
  onClose: () => void
}

/**
 * The selected attempt's bounded evidence metadata, fetched when it mounts (the parent keys it by
 * run and attempt) plus the on-demand sealed artifact viewer. Everything shown is historical
 * evidence about that past attempt — never the run's current state, a live tail, or proof of what a
 * provider account allows.
 */
function AgentAttemptDetail({ runId, attemptId, onClose }: AgentAttemptDetailProps) {
  const { state, retry } = useAgentAttemptEvidence(runId, attemptId)

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
      {state.status === 'success' && <AgentAttemptEvidenceBody runId={runId} evidence={state.evidence} />}
    </section>
  )
}

function AgentAttemptEvidenceBody({ runId, evidence }: { runId: string; evidence: AgentAttemptEvidenceResponse }) {
  const artifacts = evidence.artifacts ?? []
  const claimed = formatUtc(evidence.claimedAtUtc)
  const completed = formatUtc(evidence.completedAtUtc)
  const lineage = describeRepairLineage(evidence.repairSourceAttemptId, evidence.repairSourceAttemptNumber)

  return (
    <div>
      <p>
        Attempt #{evidence.attemptNumber} · {evidence.role} · {evidence.provider} · {evidence.attemptStatus}
        {evidence.outcome ? ` · Result: ${evidence.outcome}` : ''}
      </p>
      {lineage && <p className="dc-attempt-history-lineage">{lineage}. Provenance only.</p>}
      {claimed && <p>Claimed {claimed}{completed ? `, ended ${completed}` : ''}.</p>}
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
      <AgentAttemptArtifactViewer runId={runId} attemptId={evidence.attemptId ?? ''} artifacts={artifacts} />
    </div>
  )
}

function AgentAttemptHistoryList({ runId }: { runId: string }) {
  const { state, loadMore } = useAgentAttemptHistory(runId)
  const [selectedAttemptId, setSelectedAttemptId] = useState<string | null>(null)

  return (
    <div className="dc-attempt-history-list">
      {state.status === 'loading' && state.items.length === 0 && <p className="dc-empty-state">Loading attempt history…</p>}
      {state.status === 'loaded' && state.items.length === 0 && (
        <p className="dc-empty-state">No Agent attempts have been recorded for this run.</p>
      )}
      {state.items.length > 0 && (
        <ol className="dc-attempt-history-items">
          {state.items.map((entry) => (
            <li key={entry.attemptId}>
              <span>{describeEntry(entry)}</span>{' '}
              {entry.identityValid === true && (
                <button
                  type="button"
                  aria-label={`Inspect attempt ${entry.attemptNumber}`}
                  aria-pressed={selectedAttemptId === entry.attemptId}
                  onClick={() => setSelectedAttemptId(entry.attemptId ?? null)}
                >
                  Inspect
                </button>
              )}
            </li>
          ))}
        </ol>
      )}
      {state.status === 'error' && (
        <p className="dc-card-evidence-error" role="status">
          {state.message}{' '}
          <button type="button" onClick={loadMore}>
            Retry
          </button>
        </p>
      )}
      {state.status === 'loading' && state.items.length > 0 && <p className="dc-empty-state">Loading older attempts…</p>}
      {state.status === 'loaded' && state.hasMore && (
        <button type="button" onClick={loadMore}>
          Load older attempts
        </button>
      )}
      {selectedAttemptId && (
        <AgentAttemptDetail
          key={`${runId}:${selectedAttemptId}`}
          runId={runId}
          attemptId={selectedAttemptId}
          onClose={() => setSelectedAttemptId(null)}
        />
      )}
    </div>
  )
}

/**
 * Read-only, on-demand history of a run's Agent attempts, newest first, with a single selected
 * attempt drill-down. Nothing is fetched until the owner opens the history; closing it (or a change
 * of run, which the parent expresses by keying this component by run) discards every loaded row,
 * the selection, and any accumulated artifact text. The history is bounded metadata — raw artifact
 * text is requested only after an attempt is selected and a purpose is chosen.
 */
export function AgentAttemptHistoryPanel({ runId }: { runId: string }) {
  const [open, setOpen] = useState(false)

  return (
    <section className="dc-attempt-history" aria-label="Agent attempt history">
      <button type="button" aria-expanded={open} onClick={() => setOpen((value) => !value)}>
        {open ? 'Hide Agent attempt history' : 'Show Agent attempt history'}
      </button>
      {open && <AgentAttemptHistoryList key={runId} runId={runId} />}
    </section>
  )
}
