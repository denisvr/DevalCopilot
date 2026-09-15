import { useProjectWorkspace } from '../hooks/useProjectWorkspace'
import { WorkspaceEvidencePanel } from './WorkspaceEvidencePanel'

interface CandidateWorkspacePanelProps {
  projectId: string
}

function abbreviate(sha: string | undefined): string {
  return sha ? sha.slice(0, 7) : ''
}

/**
 * The isolated candidate workspace this project may prepare — deliberately a visually distinct
 * sibling of `ProjectBaselineSummary`, never merged into it: the two represent different truths
 * (the user's stable checkout vs. a tool-owned, isolated candidate) that must never be shown in
 * a way that could be confused with one another.
 */
export function CandidateWorkspacePanel({ projectId }: CandidateWorkspacePanelProps) {
  const { workspace, loading, preparing, rechecking, error, prepare, recheckIdentity } = useProjectWorkspace(projectId)

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

      <WorkspaceEvidencePanel projectId={projectId} workspaceReady={workspace.state === 'Ready'} />

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
