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
import { useCodeReviewAttemptStatus } from '../hooks/useCodeReviewAttemptStatus'
import { useRequestCodeReview } from '../hooks/useRequestCodeReview'
import { useReviewCorrectionAttemptStatus } from '../hooks/useReviewCorrectionAttemptStatus'
import { useRequestReviewCorrection } from '../hooks/useRequestReviewCorrection'
import { useAuthorizeReviewCorrection } from '../hooks/useAuthorizeReviewCorrection'
import { selectCurrentProcessAttemptId } from '../selectCurrentProcessAttempt'
import { selectLatestCodexProposalMessageId } from '../selectLatestCodexProposal'
import { selectLatestExecutionReportMessageId } from '../selectLatestExecutionReport'
import { AgentClaimBudgetBanner } from './AgentClaimBudgetBanner'
import { AgentInvocationTimeBudgetBanner } from './AgentInvocationTimeBudgetBanner'
import { AgentProcessDurationSummaryBanner } from './AgentProcessDurationSummaryBanner'
import { AgentCollaboration } from './AgentCollaboration'
import { CodexPlanningAction } from './CodexPlanningAction'
import { ClaudeCriticalReviewAction } from './ClaudeCriticalReviewAction'
import { ChallengeResolutionAction } from './ChallengeResolutionAction'
import { ImplementationAction } from './ImplementationAction'
import { CodeReviewAction } from './CodeReviewAction'
import { ReviewCorrectionAction } from './ReviewCorrectionAction'
import { ConnectionBanner } from './ConnectionBanner'
import { LatestAgentAttemptEvidence } from './LatestAgentAttemptEvidence'
import { LiveOutputDrawer } from './LiveOutputDrawer'
import { RunHeader } from './RunHeader'
import { RunTokenUsageSummary } from './RunTokenUsageSummary'
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
  const codeReviewAttemptStatus = useCodeReviewAttemptStatus(runId, cockpit?.latestSequence)
  const requestCodeReview = useRequestCodeReview(codeReviewAttemptStatus.refresh)
  const reviewCorrectionAttemptStatus = useReviewCorrectionAttemptStatus(runId, cockpit?.latestSequence)
  const requestReviewCorrection = useRequestReviewCorrection(runId, reviewCorrectionAttemptStatus.refresh)
  const authorizeReviewCorrection = useAuthorizeReviewCorrection(runId, reviewCorrectionAttemptStatus.refresh)
  const latestCodexProposalMessageId = selectLatestCodexProposalMessageId(collaborationTimeline.cards)
  // The backend independently re-verifies this eligibility in full before ever acting on it;
  // this is only a display hint. A failed or active correction must never resurrect the stale
  // initial report, while a durable correction (including a competing InputAlreadyCorrected
  // result) makes the newest timeline report the next review candidate.
  const latestExecutionReportMessageId = selectLatestExecutionReportMessageId(
    reviewCorrectionAttemptStatus.status?.reviewableExecutionReportMessageId,
    reviewCorrectionAttemptStatus.loading,
    reviewCorrectionAttemptStatus.error,
  )
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
      {/* A cockpit projection still held from the previously selected run is never rendered as
          evidence for the newly selected one. */}
      {cockpit.runId === runId && (
        <AgentClaimBudgetBanner
          maximumAgentAttempts={cockpit.maximumAgentAttempts ?? 0}
          agentAttemptsUsed={cockpit.agentAttemptsUsed ?? 0}
          agentBudgetExhausted={cockpit.agentBudgetExhausted ?? false}
        />
      )}
      {cockpit.runId === runId && (
        <AgentInvocationTimeBudgetBanner
          maximumMilliseconds={cockpit.agentInvocationTimeBudget?.maximumMilliseconds}
          reservedMilliseconds={cockpit.agentInvocationTimeBudget?.reservedMilliseconds}
          remainingMilliseconds={cockpit.agentInvocationTimeBudget?.remainingMilliseconds}
          isLegacyUnknown={cockpit.agentInvocationTimeBudget?.isLegacyUnknown ?? false}
          evidenceInvalid={cockpit.agentInvocationTimeBudget?.evidenceInvalid ?? false}
        />
      )}
      {cockpit.runId === runId && (
        <AgentProcessDurationSummaryBanner
          status={cockpit.agentProcessDurationSummary?.status}
          totalMeasuredMilliseconds={cockpit.agentProcessDurationSummary?.totalMeasuredMilliseconds}
          dispatchedAttemptCount={cockpit.agentProcessDurationSummary?.dispatchedAttemptCount}
          pendingAttemptCount={cockpit.agentProcessDurationSummary?.pendingAttemptCount}
          validEvidenceCount={cockpit.agentProcessDurationSummary?.validEvidenceCount}
          malformedEvidenceCount={cockpit.agentProcessDurationSummary?.malformedEvidenceCount}
        />
      )}
      <LatestAgentAttemptEvidence attempt={cockpit.runId === runId ? cockpit.latestAgentAttempt : null} />
      <RunTokenUsageSummary summary={cockpit.runId === runId ? cockpit.tokenUsageSummary : null} />
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
          <CodeReviewAction
            executionReportMessageId={latestExecutionReportMessageId}
            status={codeReviewAttemptStatus.status}
            statusLoading={codeReviewAttemptStatus.loading}
            statusError={codeReviewAttemptStatus.error}
            requesting={requestCodeReview.requesting}
            requestError={requestCodeReview.error}
            onRequest={() =>
              latestExecutionReportMessageId && void requestCodeReview.request(runId, latestExecutionReportMessageId)
            }
          />
          <ReviewCorrectionAction
            reviewAttemptId={codeReviewAttemptStatus.status?.attemptId ?? null}
            reviewOutcome={codeReviewAttemptStatus.status?.outcome ?? null}
            status={reviewCorrectionAttemptStatus.status}
            statusLoading={reviewCorrectionAttemptStatus.loading}
            statusError={reviewCorrectionAttemptStatus.error}
            requesting={requestReviewCorrection.requesting}
            requestError={requestReviewCorrection.error}
            authorizing={authorizeReviewCorrection.authorizing}
            authorizationError={authorizeReviewCorrection.error}
            onAuthorize={() => {
              const escalationId = reviewCorrectionAttemptStatus.status?.escalationId
              if (escalationId) void authorizeReviewCorrection.authorize(runId, escalationId)
            }}
            onRequest={() => {
              const reviewAttemptId = codeReviewAttemptStatus.status?.attemptId
              if (reviewAttemptId) void requestReviewCorrection.request(runId, reviewAttemptId)
            }}
          />
          <AgentCollaboration runId={runId} {...collaborationTimeline} />
        </div>
        <UsageEvidenceRail />
      </div>
      <LiveOutputDrawer runId={runId} attemptId={currentProcessAttemptId} />
    </>
  )
}
