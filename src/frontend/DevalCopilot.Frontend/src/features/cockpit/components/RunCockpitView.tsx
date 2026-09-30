import { useRunCockpit } from '../hooks/useRunCockpit'
import { useCollaborationTimeline } from '../hooks/useCollaborationTimeline'
import { useAgentAttemptStatus } from '../hooks/useAgentAttemptStatus'
import { useRequestCodexPlanningAttempt } from '../hooks/useRequestCodexPlanningAttempt'
import { useRequestCodexPlanningRepairAttempt } from '../hooks/useRequestCodexPlanningRepairAttempt'
import { useClaudeCriticalReviewAttemptStatus } from '../hooks/useClaudeCriticalReviewAttemptStatus'
import { useRequestClaudeCriticalReview } from '../hooks/useRequestClaudeCriticalReview'
import { useRequestClaudeCriticalReviewRepairAttempt } from '../hooks/useRequestClaudeCriticalReviewRepairAttempt'
import { useChallengeResolutionAttemptStatus } from '../hooks/useChallengeResolutionAttemptStatus'
import { useRequestChallengeResolution } from '../hooks/useRequestChallengeResolution'
import { useRequestChallengeResolutionRepairAttempt } from '../hooks/useRequestChallengeResolutionRepairAttempt'
import { useImplementationAttemptStatus } from '../hooks/useImplementationAttemptStatus'
import { useRequestImplementation } from '../hooks/useRequestImplementation'
import { useCodeReviewAttemptStatus } from '../hooks/useCodeReviewAttemptStatus'
import { useRequestCodeReview } from '../hooks/useRequestCodeReview'
import { useRequestCodeReviewRepairAttempt } from '../hooks/useRequestCodeReviewRepairAttempt'
import { useReviewCorrectionAttemptStatus } from '../hooks/useReviewCorrectionAttemptStatus'
import { useRequestReviewCorrection } from '../hooks/useRequestReviewCorrection'
import { useAuthorizeReviewCorrection } from '../hooks/useAuthorizeReviewCorrection'
import { deriveGlobalAgentClaimBlock } from '../deriveGlobalAgentClaimBlock'
import { deriveAgentClaimPathTimeFit } from '../deriveAgentClaimPathTimeFit'
import { selectCurrentProcessAttemptId } from '../selectCurrentProcessAttempt'
import {
  derivePlanningLineage,
  selectImplementablePlanMessageId,
  selectReviewableProposalMessageId,
} from '../derivePlanningLineage'
import { selectLatestExecutionReportMessageId } from '../selectLatestExecutionReport'
import { AgentClaimBudgetBanner } from './AgentClaimBudgetBanner'
import { CodexAssignmentPreferenceControl } from './CodexAssignmentPreferenceControl'
import { ClaudeModelPreferenceControl } from './ClaudeModelPreferenceControl'
import { ClaudeMutationTurnLimitControl } from './ClaudeMutationTurnLimitControl'
import { TokenStopPanel } from './TokenStopPanel'
import { TokenWarningPanel } from './TokenWarningPanel'
import { AgentInvocationTimeBudgetBanner } from './AgentInvocationTimeBudgetBanner'
import { AgentProcessDurationSummaryBanner } from './AgentProcessDurationSummaryBanner'
import { AgentCollaboration } from './AgentCollaboration'
import { CodexPlanningAction } from './CodexPlanningAction'
import { CodexPlanningRepairAction } from './CodexPlanningRepairAction'
import { ClaudeCriticalReviewAction } from './ClaudeCriticalReviewAction'
import { ClaudeCriticalReviewRepairAction } from './ClaudeCriticalReviewRepairAction'
import { PlanningLineageSummary } from './PlanningLineageSummary'
import { ChallengeResolutionAction } from './ChallengeResolutionAction'
import { ChallengeResolutionRepairAction } from './ChallengeResolutionRepairAction'
import { ImplementationAction } from './ImplementationAction'
import { CodeReviewAction } from './CodeReviewAction'
import { CodeReviewRepairAction } from './CodeReviewRepairAction'
import { ReviewCorrectionAction } from './ReviewCorrectionAction'
import { ConnectionBanner } from './ConnectionBanner'
import { LatestAgentAttemptEvidence } from './LatestAgentAttemptEvidence'
import { LiveOutputDrawer } from './LiveOutputDrawer'
import { OneAgentClaimSlotRemainingWarning } from './OneAgentClaimSlotRemainingWarning'
import { ProviderTokenUsageSummaries } from './ProviderTokenUsageSummaries'
import { RunHeader } from './RunHeader'
import { RunTokenUsageSummary } from './RunTokenUsageSummary'
import { UsageEvidenceRail } from './UsageEvidenceRail'
import { WorkflowRail } from './WorkflowRail'

interface RunCockpitViewProps {
  runId: string
}

export function RunCockpitView({ runId }: RunCockpitViewProps) {
  const { cockpit, cards, connection, loading, error, syncError, refresh } = useRunCockpit(runId)
  const collaborationTimeline = useCollaborationTimeline(runId, cockpit?.latestSequence)
  const agentAttemptStatus = useAgentAttemptStatus(runId, cockpit?.latestSequence)
  const requestCodexPlanningAttempt = useRequestCodexPlanningAttempt(agentAttemptStatus.refresh)
  const requestCodexPlanningRepair = useRequestCodexPlanningRepairAttempt(runId, agentAttemptStatus.refresh)
  const claudeCriticalReviewAttemptStatus = useClaudeCriticalReviewAttemptStatus(runId, cockpit?.latestSequence)
  const requestClaudeCriticalReview = useRequestClaudeCriticalReview(claudeCriticalReviewAttemptStatus.refresh)
  const requestClaudeCriticalReviewRepair = useRequestClaudeCriticalReviewRepairAttempt(runId, claudeCriticalReviewAttemptStatus.refresh)
  const challengeResolutionAttemptStatus = useChallengeResolutionAttemptStatus(runId, cockpit?.latestSequence)
  const requestChallengeResolution = useRequestChallengeResolution(challengeResolutionAttemptStatus.refresh)
  const requestChallengeResolutionRepair = useRequestChallengeResolutionRepairAttempt(runId, challengeResolutionAttemptStatus.refresh)
  const implementationAttemptStatus = useImplementationAttemptStatus(runId, cockpit?.latestSequence)
  const requestImplementation = useRequestImplementation(implementationAttemptStatus.refresh)
  const codeReviewAttemptStatus = useCodeReviewAttemptStatus(runId, cockpit?.latestSequence)
  const requestCodeReview = useRequestCodeReview(codeReviewAttemptStatus.refresh)
  const requestCodeReviewRepair = useRequestCodeReviewRepairAttempt(runId, codeReviewAttemptStatus.refresh)
  const reviewCorrectionAttemptStatus = useReviewCorrectionAttemptStatus(runId, cockpit?.latestSequence)
  const requestReviewCorrection = useRequestReviewCorrection(runId, reviewCorrectionAttemptStatus.refresh)
  const authorizeReviewCorrection = useAuthorizeReviewCorrection(runId, reviewCorrectionAttemptStatus.refresh)
  // The newest Planner root and its Resolver revisions, read from the loaded timeline. Display hints
  // only: the backend re-decides review, resolution, and implementation eligibility from durable
  // identity, so an unverifiable chain here withholds an action and never grants one.
  const planningLineage = derivePlanningLineage(collaborationTimeline.cards)
  const reviewableProposalMessageId = selectReviewableProposalMessageId(planningLineage)
  const reviewStatusHint = {
    status: claudeCriticalReviewAttemptStatus.status,
    loading: claudeCriticalReviewAttemptStatus.loading,
    error: claudeCriticalReviewAttemptStatus.error,
  }
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
  // The one plan offered for implementation, identified only by its Proposal message id — never
  // reconstructed from display text. An Accepted original root, or a first revision that its own
  // optional second review did not challenge; never a second revision (its lineage ended in a human
  // escalation) and never while the second-review status is unknown. The backend independently
  // re-verifies this eligibility in full before acting on it — this only withholds a certainly
  // blocked action.
  const eligiblePlanProposalMessageId = selectImplementablePlanMessageId(planningLineage, reviewStatusHint)

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
  // Recomputed synchronously during render from the currently selected `runId`, never from an
  // effect: a cockpit projection still describing the previously selected run must never be
  // read as this run's budget state, even for one render frame (see `deriveGlobalAgentClaimBlock`).
  const globalClaimBlock = deriveGlobalAgentClaimBlock(cockpit, runId)
  // A separate, additive per-claim-path signal: whether THIS SPECIFIC claim path's own
  // configured invocation timeout fits the run's remaining reserved time. Computed synchronously
  // during render from the same currently selected `runId`, for the exact same stale-projection
  // reason as `globalClaimBlock` above.
  const codexPlanningTimeFit = deriveAgentClaimPathTimeFit(cockpit, runId, 'CodexPlanning')
  const claudeCriticalReviewTimeFit = deriveAgentClaimPathTimeFit(cockpit, runId, 'ClaudeCriticalReview')
  const challengeResolutionTimeFit = deriveAgentClaimPathTimeFit(cockpit, runId, 'ChallengeResolution')
  const implementationTimeFit = deriveAgentClaimPathTimeFit(cockpit, runId, 'Implementation')
  const codeReviewTimeFit = deriveAgentClaimPathTimeFit(cockpit, runId, 'CodeReview')
  const reviewCorrectionTimeFit = deriveAgentClaimPathTimeFit(cockpit, runId, 'ReviewCorrection')

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
      {cockpit.runId === runId && <OneAgentClaimSlotRemainingWarning cockpit={cockpit} selectedRunId={runId} />}
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
      {cockpit.runId === runId && (
        <CodexAssignmentPreferenceControl
          runId={runId}
          requestedCodexModel={cockpit.requestedCodexModel ?? null}
          requestedCodexEffort={cockpit.requestedCodexEffort ?? null}
        />
      )}
      {cockpit.runId === runId && (
        <ClaudeModelPreferenceControl
          key={`${runId}:${cockpit.requestedClaudeModel ?? ''}:${cockpit.requestedClaudeEffort ?? ''}`}
          runId={runId}
          requestedClaudeModel={cockpit.requestedClaudeModel ?? null}
          requestedClaudeEffort={cockpit.requestedClaudeEffort ?? null}
          onSaved={refresh}
        />
      )}
      {cockpit.runId === runId && (
        <ClaudeMutationTurnLimitControl
          key={`turn-limit:${runId}:${cockpit.claudeMutationTurnLimit?.state ?? ''}:${cockpit.claudeMutationTurnLimit?.maxTurns ?? ''}`}
          runId={runId}
          request={cockpit.claudeMutationTurnLimit}
          editable={cockpit.lifecycle === 'Created' || cockpit.lifecycle === 'Running'}
          onSaved={refresh}
        />
      )}
      {cockpit.runId === runId && <TokenWarningPanel key={runId} runId={runId} tokenWarnings={cockpit.tokenWarnings} onSaved={refresh} />}
      {cockpit.runId === runId && <TokenStopPanel key={`token-stop:${runId}`} runId={runId} tokenStops={cockpit.tokenStops} onSaved={refresh} />}
      <LatestAgentAttemptEvidence attempt={cockpit.runId === runId ? cockpit.latestAgentAttempt : null} />
      <RunTokenUsageSummary summary={cockpit.runId === runId ? cockpit.tokenUsageSummary : null} />
      <ProviderTokenUsageSummaries entries={cockpit.runId === runId ? cockpit.providerTokenUsageSummaries : null} />
      <div className="dc-workspace">
        <WorkflowRail stageMap={cockpit.stageMap ?? []} />
        <div className="dc-collaboration-column">
          <CodexPlanningAction
            status={agentAttemptStatus.status}
            statusLoading={agentAttemptStatus.loading}
            statusError={agentAttemptStatus.error}
            requesting={requestCodexPlanningAttempt.requesting || requestCodexPlanningRepair.requesting}
            requestError={requestCodexPlanningAttempt.error}
            onRequest={() => void requestCodexPlanningAttempt.request(runId)}
            globalClaimBlock={globalClaimBlock}
            timeFit={codexPlanningTimeFit}
          />
          <CodexPlanningRepairAction
            status={agentAttemptStatus.status}
            statusLoading={agentAttemptStatus.loading}
            ordinaryRequesting={requestCodexPlanningAttempt.requesting}
            repairRequesting={requestCodexPlanningRepair.requesting}
            repairError={requestCodexPlanningRepair.error}
            onRequestRepair={(sourceAttemptId) => void requestCodexPlanningRepair.request(sourceAttemptId)}
            globalClaimBlock={globalClaimBlock}
            timeFit={codexPlanningTimeFit}
          />
          <ClaudeCriticalReviewAction
            proposalMessageId={reviewableProposalMessageId}
            proposalKind={planningLineage.firstRevision ? 'revision' : 'original'}
            status={claudeCriticalReviewAttemptStatus.status}
            statusLoading={claudeCriticalReviewAttemptStatus.loading}
            statusError={claudeCriticalReviewAttemptStatus.error}
            requesting={requestClaudeCriticalReview.requesting || requestClaudeCriticalReviewRepair.requesting}
            requestError={requestClaudeCriticalReview.error}
            onRequest={() =>
              reviewableProposalMessageId && void requestClaudeCriticalReview.request(runId, reviewableProposalMessageId)
            }
            globalClaimBlock={globalClaimBlock}
            timeFit={claudeCriticalReviewTimeFit}
          />
          <ClaudeCriticalReviewRepairAction
            status={claudeCriticalReviewAttemptStatus.status}
            statusLoading={claudeCriticalReviewAttemptStatus.loading}
            ordinaryRequesting={requestClaudeCriticalReview.requesting}
            repairRequesting={requestClaudeCriticalReviewRepair.requesting}
            repairError={requestClaudeCriticalReviewRepair.error}
            onRequestRepair={(sourceAttemptId) => void requestClaudeCriticalReviewRepair.request(sourceAttemptId)}
            globalClaimBlock={globalClaimBlock}
            timeFit={claudeCriticalReviewTimeFit}
          />
          <PlanningLineageSummary lineage={planningLineage} review={reviewStatusHint} />
          <ChallengeResolutionAction
            challengedReviewAttemptId={latestChallengedReviewAttemptId}
            reviewedProposalMessageId={latestChallengedReviewProposalId}
            status={challengeResolutionAttemptStatus.status}
            statusLoading={challengeResolutionAttemptStatus.loading}
            statusError={challengeResolutionAttemptStatus.error}
            requesting={requestChallengeResolution.requesting || requestChallengeResolutionRepair.requesting}
            requestError={requestChallengeResolution.error}
            onRequest={() =>
              latestChallengedReviewAttemptId && void requestChallengeResolution.request(runId, latestChallengedReviewAttemptId)
            }
            globalClaimBlock={globalClaimBlock}
            timeFit={challengeResolutionTimeFit}
          />
          <ChallengeResolutionRepairAction
            status={challengeResolutionAttemptStatus.status}
            statusLoading={challengeResolutionAttemptStatus.loading}
            ordinaryRequesting={requestChallengeResolution.requesting}
            repairRequesting={requestChallengeResolutionRepair.requesting}
            repairError={requestChallengeResolutionRepair.error}
            onRequestRepair={(sourceAttemptId) => void requestChallengeResolutionRepair.request(sourceAttemptId)}
            globalClaimBlock={globalClaimBlock}
            timeFit={challengeResolutionTimeFit}
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
            globalClaimBlock={globalClaimBlock}
            timeFit={implementationTimeFit}
          />
          <CodeReviewAction
            executionReportMessageId={latestExecutionReportMessageId}
            status={codeReviewAttemptStatus.status}
            statusLoading={codeReviewAttemptStatus.loading}
            statusError={codeReviewAttemptStatus.error}
            requesting={requestCodeReview.requesting || requestCodeReviewRepair.requesting}
            requestError={requestCodeReview.error}
            onRequest={() =>
              latestExecutionReportMessageId && void requestCodeReview.request(runId, latestExecutionReportMessageId)
            }
            globalClaimBlock={globalClaimBlock}
            timeFit={codeReviewTimeFit}
          />
          <CodeReviewRepairAction
            status={codeReviewAttemptStatus.status}
            statusLoading={codeReviewAttemptStatus.loading}
            ordinaryRequesting={requestCodeReview.requesting}
            repairRequesting={requestCodeReviewRepair.requesting}
            repairError={requestCodeReviewRepair.error}
            onRequestRepair={(sourceAttemptId) => void requestCodeReviewRepair.request(sourceAttemptId)}
            globalClaimBlock={globalClaimBlock}
            timeFit={codeReviewTimeFit}
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
            globalClaimBlock={globalClaimBlock}
            timeFit={reviewCorrectionTimeFit}
            onAuthorize={() => {
              const escalationId = reviewCorrectionAttemptStatus.status?.escalationId
              if (escalationId) void authorizeReviewCorrection.authorize(runId, escalationId)
            }}
            onAuthorizeWithGuidance={async (guidance) => {
              const escalationId = reviewCorrectionAttemptStatus.status?.escalationId
              return escalationId ? authorizeReviewCorrection.authorize(runId, escalationId, guidance) : false
            }}
            onRequest={() => {
              const reviewAttemptId = codeReviewAttemptStatus.status?.attemptId
              if (reviewAttemptId) void requestReviewCorrection.request(runId, reviewAttemptId)
            }}
          />
          <AgentCollaboration runId={runId} {...collaborationTimeline} />
        </div>
        <UsageEvidenceRail runId={runId} />
      </div>
      <LiveOutputDrawer runId={runId} attemptId={currentProcessAttemptId} />
    </>
  )
}
