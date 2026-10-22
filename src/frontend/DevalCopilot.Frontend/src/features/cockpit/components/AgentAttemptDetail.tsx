import type { AgentAttemptEvidenceResponse } from '../../../api/clients'
import { useAgentAttemptEvidence } from '../hooks/useAgentAttemptEvidence'
import type { AgentAttemptArtifactWindowTarget } from '../hooks/useAgentAttemptArtifactWindow'
import { AgentAttemptArtifactViewer } from './AgentAttemptArtifactViewer'
import { ClaudeTurnLimitFacts } from './ClaudeTurnLimitFacts'
import { DirectGuidanceFact } from './DirectGuidanceFact'
import { ModelContextLimitsFact } from './ModelContextLimitsFact'
import { CodexAccountUsageDecisionFact } from './CodexAccountUsageDecisionFact'
import { SelectedAttemptExecutionFacts } from './SelectedAttemptExecutionFacts'
import { SelectedAttemptRecordedInputs } from './SelectedAttemptRecordedInputs'
import { SelectedAttemptRecordedOutputs } from './SelectedAttemptRecordedOutputs'
import { SelectedAttemptRecordedVerification } from './SelectedAttemptRecordedVerification'
import { ARTIFACT_PURPOSE_LABELS } from '../artifactPurposes'
import { describeRepairLineage } from '../describeRepairLineage'
import type { AgentAttemptIdentityExpectation } from '../agentAttemptHistory'

export interface AgentAttemptDetailProps {
  selectionKey: string
  runId: string
  /** What the evidence must match: the selected row's identity, number and immutable role, provider and response contract. */
  expected: AgentAttemptIdentityExpectation
  onClose: () => void
}

/**
 * The selected attempt's bounded evidence metadata, read when its selection lifetime begins, plus the on-demand sealed artifact viewer
 * and the on-demand recorded collaboration inputs and outputs, which need neither the evidence answer nor each other.
 * Everything shown is historical evidence about that past attempt — never the run's current state, a live tail, or proof of what a
 * provider account allows.
 */
export function AgentAttemptDetail({ selectionKey, runId, expected, onClose }: AgentAttemptDetailProps) {
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
      <SelectedAttemptRecordedInputs selectionKey={selectionKey} runId={runId} expected={expected} />
      <SelectedAttemptRecordedOutputs selectionKey={selectionKey} runId={runId} expected={expected} />
      <SelectedAttemptRecordedVerification selectionKey={selectionKey} runId={runId} expected={expected} />
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
