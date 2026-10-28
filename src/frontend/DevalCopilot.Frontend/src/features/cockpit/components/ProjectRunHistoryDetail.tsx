import { deriveRunExecutionModeDisclosure } from '../deriveRunExecutionModeDisclosure'
import { lifecycleLabel, stageLabel } from '../projectRunHistory'
import type { RunHistoryEntry } from '../projectRunHistory'
import { toUtcText } from '../utcText'
import { AgentAttemptHistoryPanel } from './AgentAttemptHistoryPanel'
import { LocalCommitInspectionRegion } from './LocalCommitInspectionRegion'
import { LocalExecutionInspectionPanel } from './LocalExecutionInspectionPanel'
import { PlanningSequenceInspectionPanel } from './PlanningSequenceInspectionPanel'
import { LocalDeliveryReceipt } from './LocalDeliveryReceipt'
import { CollaborationHistoryPanel } from './CollaborationHistoryPanel'
import { RunEventHistoryPanel } from './RunEventHistoryPanel'

const SNAPSHOT_NOTE =
  'This is a snapshot of what the host recorded for this run when the history was read. It is not live progress, the current workspace or current verification, and it says nothing about remote publication. A last-advanced time is the time recorded when the run was last advanced, not a sign that work is still in progress.'
const NO_SOURCE =
  'No completed delivery source is available in this view. That does not show that no local commit operation exists, that a delivery failed, or that one succeeded.'

function Fact({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <>
      <dt>{label}</dt>
      <dd>{children}</dd>
    </>
  )
}

interface ProjectRunHistoryDetailProps {
  entry: RunHistoryEntry
  onClose: () => void
}

/**
 * The recorded metadata of one historical run, when the host located a delivery source for it the existing local-delivery receipt
 * for exactly that source, and collapsed, on-demand lists of the run's recorded event headers (ADR-0034), recorded collaboration messages (ADR-0035) and Agent
 * attempts with their evidence and sealed artifact windows (including an attempt that emitted no message) for exactly this run. It mounts no live cockpit,
 * eligibility, configuration or mutation control. Every value is rendered as text.
 */
export function ProjectRunHistoryDetail({ entry, onClose }: ProjectRunHistoryDetailProps) {
  const source = entry.receiptSource

  return (
    <section className="dc-run-history-detail" aria-label="Selected run">
      <h4>Run #{entry.executionNumber} — recorded metadata</h4>
      <p className="dc-local-commit-note">{SNAPSHOT_NOTE}</p>
      <dl>
        <Fact label="Objective">{entry.objective}</Fact>
        <Fact label="Run">{entry.runId}</Fact>
        <Fact label="Lifecycle (recorded)">{lifecycleLabel(entry.lifecycle)}</Fact>
        <Fact label="Stage (recorded)">{stageLabel(entry.stage)}</Fact>
        <Fact label="Execution mode">{deriveRunExecutionModeDisclosure(entry.executionMode).label}</Fact>
        <Fact label="Created (UTC)">{toUtcText(entry.createdAtUtc)}</Fact>
        <Fact label="Last advanced (UTC)">{toUtcText(entry.lastAdvancedAtUtc)}</Fact>
        {source ? (
          <Fact label="Recorded local commit source">
            commit {source.commitSha}, operation {source.operationId}, checkpoint #{source.checkpointNumber} ({source.checkpointId})
          </Fact>
        ) : null}
      </dl>
      {source ? <LocalDeliveryReceipt source={source} /> : <p>{NO_SOURCE}</p>}
      <LocalCommitInspectionRegion runId={entry.runId} projectId={entry.projectId} />
      <RunEventHistoryPanel runId={entry.runId} />
      <CollaborationHistoryPanel runId={entry.runId} />
      <AgentAttemptHistoryPanel runId={entry.runId} />
      <LocalExecutionInspectionPanel runId={entry.runId} />
      <PlanningSequenceInspectionPanel runId={entry.runId} />
      <p>
        <button type="button" onClick={onClose}>
          Close selected run
        </button>
      </p>
    </section>
  )
}
