import { useRunCockpit } from '../hooks/useRunCockpit'
import { useCollaborationTimeline } from '../hooks/useCollaborationTimeline'
import { useAgentAttemptStatus } from '../hooks/useAgentAttemptStatus'
import { useRequestCodexPlanningAttempt } from '../hooks/useRequestCodexPlanningAttempt'
import { useClaudeCriticalReviewAttemptStatus } from '../hooks/useClaudeCriticalReviewAttemptStatus'
import { useRequestClaudeCriticalReview } from '../hooks/useRequestClaudeCriticalReview'
import { selectCurrentProcessAttemptId } from '../selectCurrentProcessAttempt'
import { selectLatestCodexProposalMessageId } from '../selectLatestCodexProposal'
import { AgentCollaboration } from './AgentCollaboration'
import { CodexPlanningAction } from './CodexPlanningAction'
import { ClaudeCriticalReviewAction } from './ClaudeCriticalReviewAction'
import { ConnectionBanner } from './ConnectionBanner'
import { LiveOutputDrawer } from './LiveOutputDrawer'
import { RunHeader } from './RunHeader'
import { UsageEvidenceRail } from './UsageEvidenceRail'
import { WorkflowRail } from './WorkflowRail'

interface RunCockpitViewProps {
  runId: string
}

export function RunCockpitView({ runId }: RunCockpitViewProps) {
  const { cockpit, cards, connection, loading, error, syncError } = useRunCockpit(runId)
  const collaborationTimeline = useCollaborationTimeline(runId, cockpit?.latestSequence)
  const agentAttemptStatus = useAgentAttemptStatus(runId, cockpit?.latestSequence)
  const requestCodexPlanningAttempt = useRequestCodexPlanningAttempt(agentAttemptStatus.refresh)
  const claudeCriticalReviewAttemptStatus = useClaudeCriticalReviewAttemptStatus(runId, cockpit?.latestSequence)
  const requestClaudeCriticalReview = useRequestClaudeCriticalReview(claudeCriticalReviewAttemptStatus.refresh)
  const latestCodexProposalMessageId = selectLatestCodexProposalMessageId(collaborationTimeline.cards)

  if (loading && !cockpit) {
    return <p className="dc-empty-state">Loading run…</p>
  }

  if (error) {
    return <p className="dc-empty-state">{error}</p>
  }

  if (!cockpit) {
    return <p className="dc-empty-state">This run could not be found.</p>
  }

  const currentProcessAttemptId = selectCurrentProcessAttemptId(cards)

  return (
    <>
      <ConnectionBanner state={connection} syncError={syncError} />
      <RunHeader cockpit={cockpit} />
      <div className="dc-workspace">
        <WorkflowRail stageMap={cockpit.stageMap ?? []} />
        <div className="dc-collaboration-column">
          <CodexPlanningAction
            status={agentAttemptStatus.status}
            statusLoading={agentAttemptStatus.loading}
            statusError={agentAttemptStatus.error}
            requesting={requestCodexPlanningAttempt.requesting}
            requestError={requestCodexPlanningAttempt.error}
            onRequest={() => void requestCodexPlanningAttempt.request(runId)}
          />
          <ClaudeCriticalReviewAction
            proposalMessageId={latestCodexProposalMessageId}
            status={claudeCriticalReviewAttemptStatus.status}
            statusLoading={claudeCriticalReviewAttemptStatus.loading}
            statusError={claudeCriticalReviewAttemptStatus.error}
            requesting={requestClaudeCriticalReview.requesting}
            requestError={requestClaudeCriticalReview.error}
            onRequest={() =>
              latestCodexProposalMessageId && void requestClaudeCriticalReview.request(runId, latestCodexProposalMessageId)
            }
          />
          <AgentCollaboration {...collaborationTimeline} />
        </div>
        <UsageEvidenceRail />
      </div>
      <LiveOutputDrawer runId={runId} attemptId={currentProcessAttemptId} />
    </>
  )
}
