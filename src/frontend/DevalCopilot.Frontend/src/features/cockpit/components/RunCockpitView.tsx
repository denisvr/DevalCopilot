import { useRunCockpit } from '../hooks/useRunCockpit'
import { useCollaborationTimeline } from '../hooks/useCollaborationTimeline'
import { useAgentAttemptStatus } from '../hooks/useAgentAttemptStatus'
import { useRequestCodexPlanningAttempt } from '../hooks/useRequestCodexPlanningAttempt'
import { useClaudeCriticalReviewAttemptStatus } from '../hooks/useClaudeCriticalReviewAttemptStatus'
import { useRequestClaudeCriticalReview } from '../hooks/useRequestClaudeCriticalReview'
import { useChallengeResolutionAttemptStatus } from '../hooks/useChallengeResolutionAttemptStatus'
import { useRequestChallengeResolution } from '../hooks/useRequestChallengeResolution'
import { useImplementationAttemptStatus } from '../hooks/useImplementationAttemptStatus'
import { useRequestImplementation } from '../hooks/useRequestImplementation'
import { selectCurrentProcessAttemptId } from '../selectCurrentProcessAttempt'
import { selectLatestCodexProposalMessageId } from '../selectLatestCodexProposal'
import { AgentCollaboration } from './AgentCollaboration'
import { CodexPlanningAction } from './CodexPlanningAction'
import { ClaudeCriticalReviewAction } from './ClaudeCriticalReviewAction'
import { ChallengeResolutionAction } from './ChallengeResolutionAction'
import { ImplementationAction } from './ImplementationAction'
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
  const challengeResolutionAttemptStatus = useChallengeResolutionAttemptStatus(runId, cockpit?.latestSequence)
  const requestChallengeResolution = useRequestChallengeResolution(challengeResolutionAttemptStatus.refresh)
  const implementationAttemptStatus = useImplementationAttemptStatus(runId, cockpit?.latestSequence)
  const requestImplementation = useRequestImplementation(implementationAttemptStatus.refresh)
  const latestCodexProposalMessageId = selectLatestCodexProposalMessageId(collaborationTimeline.cards)
  // Only the latest Claude critical-review attempt's own Challenged outcome ever makes a
  // resolution requestable — never an older, since-superseded review, and never a review still
  // Running or one that settled as Accepted.
  const latestChallengedReviewAttemptId =
    claudeCriticalReviewAttemptStatus.status?.outcome === 'Challenged' ? claudeCriticalReviewAttemptStatus.status.attemptId ?? null : null
  const latestChallengedReviewProposalId =
    claudeCriticalReviewAttemptStatus.status?.outcome === 'Challenged'
      ? claudeCriticalReviewAttemptStatus.status.reviewedProposalMessageId ?? null
      : null
  // The one authoritative resolved plan eligible for implementation, identified only by its
  // Proposal message id — never reconstructed from display text. `latestCodexProposalMessageId`
  // already tracks the highest-sequence real Codex Proposal, which becomes the resolver's
  // revised Proposal once a resolution completes, so the same selector serves both eligible
  // forms: an accepted original Proposal (no resolution has happened yet), or a resolved
  // revised Proposal (the latest Proposal now belongs to the completed resolution attempt).
  // The backend independently re-verifies this eligibility in full before ever acting on it —
  // this is only a display hint for when to offer the action.
  const isAcceptedOriginalProposal =
    claudeCriticalReviewAttemptStatus.status?.outcome === 'Accepted'
    && claudeCriticalReviewAttemptStatus.status.reviewedProposalMessageId === latestCodexProposalMessageId
  const isResolvedRevisedProposal = challengeResolutionAttemptStatus.status?.outcome === 'Resolved'
  const eligiblePlanProposalMessageId =
    isAcceptedOriginalProposal || isResolvedRevisedProposal ? latestCodexProposalMessageId : null

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
          <ChallengeResolutionAction
            challengedReviewAttemptId={latestChallengedReviewAttemptId}
            reviewedProposalMessageId={latestChallengedReviewProposalId}
            status={challengeResolutionAttemptStatus.status}
            statusLoading={challengeResolutionAttemptStatus.loading}
            statusError={challengeResolutionAttemptStatus.error}
            requesting={requestChallengeResolution.requesting}
            requestError={requestChallengeResolution.error}
            onRequest={() =>
              latestChallengedReviewAttemptId && void requestChallengeResolution.request(runId, latestChallengedReviewAttemptId)
            }
          />
          <ImplementationAction
            planProposalMessageId={eligiblePlanProposalMessageId}
            status={implementationAttemptStatus.status}
            statusLoading={implementationAttemptStatus.loading}
            statusError={implementationAttemptStatus.error}
            requesting={requestImplementation.requesting}
            requestError={requestImplementation.error}
            onRequest={() =>
              eligiblePlanProposalMessageId && void requestImplementation.request(runId, eligiblePlanProposalMessageId)
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
