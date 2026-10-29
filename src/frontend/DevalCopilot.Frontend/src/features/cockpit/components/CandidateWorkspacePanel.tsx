import { useCallback } from 'react'
import { useProjectWorkspace } from '../hooks/useProjectWorkspace'
import { useOwnedLifetime, useOwnedState } from '../hooks/useOwnedLifetime'
import { WorkspaceEvidencePanel } from './WorkspaceEvidencePanel'
import { VerificationCommandsPanel } from './VerificationCommandsPanel'
import { ProjectVerificationInspectionPanel } from './ProjectVerificationInspectionPanel'
import { CheckpointReviewPanel } from './CheckpointReviewPanel'
import { WorkspaceExecutionGuidance } from './WorkspaceExecutionGuidance'
import { ExecutionCapacityGuidance } from './ExecutionCapacityGuidance'
import { EXECUTION_CAPACITY_WITHHELD_POINTER, deriveExecutionCapacityStartGate } from '../executionCapacity'
import type { ExecutionCapacityState } from '../executionCapacity'
import { WORKSPACE_EXECUTION_WITHHELD_POINTER, deriveWorkspaceExecutionStartGate } from '../workspaceExecution'
import type { UseLocalExecutionSequenceResult } from '../hooks/useLocalExecutionSequence'
import { describeLocalExecutionSequenceWithholding } from '../localExecutionSequenceCopy'
import type { WorkspaceExecutionState } from '../workspaceExecution'

interface CandidateWorkspacePanelProps {
  projectId: string
  /**
   * Told once per explicit Refresh evidence and once per accepted complete-set approval, so the project's run can read its
   * verification-dependent status again. A hint only: it carries no data and no authority. The owner binds it to the run it serves,
   * and the approval keeps the callback it was given when it began.
   */
  onEvidenceRefreshRequested?: () => void
  /** The fixed reason a planning sequence withholds verification Run for this project's run (ADR-0055); null or absent withholds nothing. */
  runWithheldReason?: string | null
  /**
   * The selected project's workspace execution status (ADR-0057), shared with the run cockpit. Anything but a validated, current Idle
   * withholds verification Run and shows one fixed guidance here; Idle enables nothing by itself. App always passes it. Only a composition
   * that does not read the status at all (a test) may omit it, and omitting it applies no hint.
   */
  /** The selected project's workspace execution status (ADR-0057). Required: null is an explicit "no state" that withholds starting. */
  workspaceExecution: WorkspaceExecutionState | null
  /**
   * The host's execution capacity (ADR-0058), owned by the authenticated host session and shared with the run cockpit. Anything but a
   * validated, current Available withholds verification Run (a configured command may mutate) and shows one fixed guidance here; Available
   * enables nothing by itself. Required: null is an explicit "no state" that withholds starting. Evidence capture and Refresh evidence are
   * never gated by it.
   */
  executionCapacity: ExecutionCapacityState | null
  /**
   * The selected run's one local-execution-sequence reader (ADR-0060), shared with the run cockpit; this region cannot read its own copy.
   * While its control is blocked or unknown, verification Run, creating, enabling, disabling or removing a verification command and
   * capturing a checkpoint are withheld. Required: null is an explicit "no state" that withholds. A project without a run passes a
   * reader whose control is `none`.
   */
  localExecutionSequence: UseLocalExecutionSequenceResult | null
}

function abbreviate(sha: string | undefined): string {
  return sha ? sha.slice(0, 7) : ''
}

// How many times the project asked its evidence consumers to read the current checkpoint again. It belongs to the current
// project's lifetime, so another project, or a return to an earlier one, starts at zero.
interface EvidenceRefresh {
  generation: number
}

function createEvidenceRefresh(): EvidenceRefresh {
  return { generation: 0 }
}

/**
 * The isolated candidate workspace this project may prepare — deliberately a visually distinct
 * sibling of `ProjectBaselineSummary`, never merged into it: the two represent different truths
 * (the user's stable checkout vs. a tool-owned, isolated candidate) that must never be shown in
 * a way that could be confused with one another.
 */
export function CandidateWorkspacePanel({
  projectId,
  onEvidenceRefreshRequested,
  runWithheldReason = null,
  workspaceExecution,
  executionCapacity,
  localExecutionSequence,
}: CandidateWorkspacePanelProps) {
  const { workspace, loading, preparing, rechecking, error, prepare, recheckIdentity } = useProjectWorkspace(projectId)
  const refreshOwner = useOwnedLifetime(projectId)
  const [{ generation }, commitRefresh] = useOwnedState(refreshOwner, createEvidenceRefresh)
  // Reads the current checkpoint metadata again in every consumer; it records no checkpoint and requests no workflow stage.
  const requestEvidenceRefresh = useCallback(
    () => commitRefresh(previous => ({ generation: previous.generation + 1 })),
    [commitRefresh],
  )

  if (loading || !workspace) {
    return (
      <div className="dc-candidate-workspace" aria-label="Candidate workspace">
        <span className="dc-candidate-workspace-title">Candidate workspace (isolated)</span>
        <span className="dc-empty-state">Loading…</span>
      </div>
    )
  }

  const needsIdentityRecheck = workspace.physicalIdentityStatus !== 'Resolved'
  // The workspace execution status is an additional veto of verification Run (ADR-0057): the planning-sequence fence keeps its own
  // reason first, and the region's one guidance is shown below, so the Run control only points to it.
  const workspaceGate = deriveWorkspaceExecutionStartGate(workspaceExecution, projectId)
  // The host's execution capacity is a third, additional veto of the same shape (ADR-0058); the earlier reasons keep their precedence.
  const capacityGate = deriveExecutionCapacityStartGate(executionCapacity)
  const localExecutionWithheld = describeLocalExecutionSequenceWithholding(localExecutionSequence?.control ?? 'unknown')
  const effectiveRunWithheldReason =
    runWithheldReason
    ?? localExecutionWithheld
    ?? (workspaceGate.allowsExecutionStart ? null : WORKSPACE_EXECUTION_WITHHELD_POINTER)
    ?? (capacityGate.allowsMutatingStart ? null : EXECUTION_CAPACITY_WITHHELD_POINTER)

  return (
    <div className="dc-candidate-workspace" aria-label="Candidate workspace">
      <span className="dc-candidate-workspace-title">Candidate workspace (isolated)</span>

      {needsIdentityRecheck ? (
        <div className="dc-candidate-workspace-blocked">
          <span>{workspace.physicalIdentityBlockedMessage ?? 'This project has not been verified for workspace preparation yet.'}</span>
          <button type="button" className="dc-button" disabled={rechecking} onClick={() => void recheckIdentity()}>
            {rechecking ? 'Rechecking…' : 'Recheck identity'}
          </button>
        </div>
      ) : null}

      {!needsIdentityRecheck && workspace.state === 'NotRequested' ? (
        <button type="button" className="dc-button" data-variant="primary" disabled={preparing} onClick={() => void prepare()}>
          {preparing ? 'Preparing…' : 'Prepare workspace'}
        </button>
      ) : null}

      {workspace.state === 'Preparing' ? <span className="dc-candidate-workspace-state">Preparing…</span> : null}

      {workspace.state === 'Ready' || workspace.state === 'NeedsAttention' ? (
        <div className="dc-candidate-workspace-details" data-state={workspace.state}>
          <span className="dc-candidate-workspace-path">{workspace.candidatePath}</span>
          <span className="dc-candidate-workspace-branch">
            {workspace.branchName} @ {abbreviate(workspace.sourceCommitSha)}
            {workspace.sourceBranchName ? ` (from ${workspace.sourceBranchName})` : ' (from a detached HEAD)'}
          </span>
          {workspace.state === 'NeedsAttention' ? (
            <span className="dc-candidate-workspace-attention">{workspace.blockedReasonMessage}</span>
          ) : null}
        </div>
      ) : null}

      {workspace.state === 'Ready' || workspace.state === 'NeedsAttention' ? (
        <button
          type="button"
          className="dc-button"
          onClick={() => {
            requestEvidenceRefresh()
            onEvidenceRefreshRequested?.()
          }}
        >
          Refresh evidence
        </button>
      ) : null}

      <WorkspaceExecutionGuidance gate={workspaceGate} state={workspaceExecution ?? null} region="candidate workspace" />
      <ExecutionCapacityGuidance gate={capacityGate} state={executionCapacity ?? null} region="candidate workspace" />

      <WorkspaceEvidencePanel
        key={`evidence:${projectId}`}
        projectId={projectId}
        workspaceReady={workspace.state === 'Ready'}
        refreshGeneration={generation}
        onCaptured={requestEvidenceRefresh}
        captureWithheldReason={localExecutionWithheld}
      />
      <VerificationCommandsPanel
        key={`commands:${projectId}`}
        projectId={projectId}
        refreshGeneration={generation}
        runWithheldReason={effectiveRunWithheldReason}
        configureWithheldReason={localExecutionWithheld}
      />
      <ProjectVerificationInspectionPanel key={`verification-history:${projectId}`} projectId={projectId} />
      <CheckpointReviewPanel
        key={`reviews:${projectId}`}
        projectId={projectId}
        refreshGeneration={generation}
        onApprovalAccepted={onEvidenceRefreshRequested}
      />

      {!needsIdentityRecheck && workspace.state === 'Blocked' ? (
        <div className="dc-candidate-workspace-blocked">
          <span>{workspace.blockedReasonMessage}</span>
          <button type="button" className="dc-button" disabled={preparing} onClick={() => void prepare()}>
            {preparing ? 'Preparing…' : 'Try again'}
          </button>
        </div>
      ) : null}

      {error ? <span className="dc-candidate-workspace-error">{error}</span> : null}
    </div>
  )
}
