import { useCallback } from 'react'
import { useProjectWorkspace } from '../hooks/useProjectWorkspace'
import { useOwnedLifetime, useOwnedState } from '../hooks/useOwnedLifetime'
import { WorkspaceEvidencePanel } from './WorkspaceEvidencePanel'
import { VerificationCommandsPanel } from './VerificationCommandsPanel'
import { CheckpointReviewPanel } from './CheckpointReviewPanel'

interface CandidateWorkspacePanelProps {
  projectId: string
  /** Told once per explicit Refresh evidence, so the project's run can read its verification-dependent status again. */
  onEvidenceRefreshRequested?: () => void
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
export function CandidateWorkspacePanel({ projectId, onEvidenceRefreshRequested }: CandidateWorkspacePanelProps) {
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

      <WorkspaceEvidencePanel
        key={`evidence:${projectId}`}
        projectId={projectId}
        workspaceReady={workspace.state === 'Ready'}
        refreshGeneration={generation}
        onCaptured={requestEvidenceRefresh}
      />
      <VerificationCommandsPanel key={`commands:${projectId}`} projectId={projectId} refreshGeneration={generation} />
      <CheckpointReviewPanel key={`reviews:${projectId}`} projectId={projectId} refreshGeneration={generation} />

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
