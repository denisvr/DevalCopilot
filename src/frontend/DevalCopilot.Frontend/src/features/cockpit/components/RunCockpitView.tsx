import { useCallback } from 'react'
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
import { usePlanningImplementationAuthorization } from '../hooks/usePlanningImplementationAuthorization'
import { useAuthorizePlanningImplementation } from '../hooks/useAuthorizePlanningImplementation'
import { useCodeReviewAttemptStatus } from '../hooks/useCodeReviewAttemptStatus'
import { useRequestCodeReview } from '../hooks/useRequestCodeReview'
import { useRequestCodeReviewRepairAttempt } from '../hooks/useRequestCodeReviewRepairAttempt'
import { useReviewCorrectionAttemptStatus } from '../hooks/useReviewCorrectionAttemptStatus'
import { useRequestReviewCorrection } from '../hooks/useRequestReviewCorrection'
import { useAuthorizeReviewCorrection } from '../hooks/useAuthorizeReviewCorrection'
import { useVerificationDiagnosisStatus } from '../hooks/useVerificationDiagnosisStatus'
import { useRequestVerificationDiagnosis } from '../hooks/useRequestVerificationDiagnosis'
import { useRequestDiagnosisCorrection } from '../hooks/useRequestDiagnosisCorrection'
import { deriveGlobalAgentClaimBlock } from '../deriveGlobalAgentClaimBlock'
import { deriveAgentClaimPathTimeFit } from '../deriveAgentClaimPathTimeFit'
import { deriveRunExecutionModeDisclosure } from '../deriveRunExecutionModeDisclosure'
import { selectCurrentProcessAttemptId } from '../selectCurrentProcessAttempt'
import {
  derivePlanningLineage,
  selectAuthorizedPlanMessageId,
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
import { PlanningImplementationAuthorizationPanel } from './PlanningImplementationAuthorizationPanel'
import { ChallengeResolutionAction } from './ChallengeResolutionAction'
import { ChallengeResolutionRepairAction } from './ChallengeResolutionRepairAction'
import { ImplementationAction } from './ImplementationAction'
import { CodeReviewAction } from './CodeReviewAction'
import { CodeReviewRepairAction } from './CodeReviewRepairAction'
import { ReviewCorrectionAction } from './ReviewCorrectionAction'
import { VerificationDiagnosisAction } from './VerificationDiagnosisAction'
import { ConnectionBanner } from './ConnectionBanner'
import { LatestAgentAttemptEvidence } from './LatestAgentAttemptEvidence'
import { LiveOutputDrawer } from './LiveOutputDrawer'
import { OneAgentClaimSlotRemainingWarning } from './OneAgentClaimSlotRemainingWarning'
import { ProviderTokenUsageSummaries } from './ProviderTokenUsageSummaries'
import { RunExecutionModeNotice } from './RunExecutionModeNotice'
import { RunHeader } from './RunHeader'
import { RunTokenUsageSummary } from './RunTokenUsageSummary'
import { UsageEvidenceRail } from './UsageEvidenceRail'
import { WorkflowRail } from './WorkflowRail'

interface RunCockpitViewProps {
  runId: string
  /** Advanced by the project's explicit Refresh evidence action (owned with this run): the verification-dependent diagnosis and
   * code-review status are read again through their existing reads. Verification completes without a run event. */
  evidenceRefreshGeneration?: number
}

const AUTHORIZED_REQUEST_LABEL = 'Implement the human-authorized final plan with Claude'
const AUTHORIZATION_SPENT_NOTE =
  'The human authorization for this final plan was already used by an implementation claim. It cannot be reused, so no new implementation of this plan can be requested through it.'

export function RunCockpitView({ runId, evidenceRefreshGeneration = 0 }: RunCockpitViewProps) {
  const { cockpit, cards, connection, loading, error, syncError, refresh } = useRunCockpit(runId)
  const collaborationTimeline = useCollaborationTimeline(runId, cockpit?.latestSequence)
  const agentAttemptStatus = useAgentAttemptStatus(runId, cockpit?.latestSequence)
  const requestCodexPlanningAttempt = useRequestCodexPlanningAttempt(runId, agentAttemptStatus.refresh)
  const requestCodexPlanningRepair = useRequestCodexPlanningRepairAttempt(runId, agentAttemptStatus.refresh)
  const claudeCriticalReviewAttemptStatus = useClaudeCriticalReviewAttemptStatus(runId, cockpit?.latestSequence)
  const requestClaudeCriticalReview = useRequestClaudeCriticalReview(runId, claudeCriticalReviewAttemptStatus.refresh)
  const requestClaudeCriticalReviewRepair = useRequestClaudeCriticalReviewRepairAttempt(runId, claudeCriticalReviewAttemptStatus.refresh)
  const challengeResolutionAttemptStatus = useChallengeResolutionAttemptStatus(runId, cockpit?.latestSequence)
  const requestChallengeResolution = useRequestChallengeResolution(runId, challengeResolutionAttemptStatus.refresh)
  const requestChallengeResolutionRepair = useRequestChallengeResolutionRepairAttempt(runId, challengeResolutionAttemptStatus.refresh)
  const implementationAttemptStatus = useImplementationAttemptStatus(runId, cockpit?.latestSequence)
  const codeReviewAttemptStatus = useCodeReviewAttemptStatus(runId, cockpit?.latestSequence, evidenceRefreshGeneration)
  const requestCodeReview = useRequestCodeReview(runId, codeReviewAttemptStatus.refresh, evidenceRefreshGeneration)
  const requestCodeReviewRepair = useRequestCodeReviewRepairAttempt(runId, codeReviewAttemptStatus.refresh)
  const reviewCorrectionAttemptStatus = useReviewCorrectionAttemptStatus(runId, cockpit?.latestSequence)
  const requestReviewCorrection = useRequestReviewCorrection(runId, codeReviewAttemptStatus.status?.attemptId ?? null, reviewCorrectionAttemptStatus.refresh)
  const authorizeReviewCorrection = useAuthorizeReviewCorrection(runId, reviewCorrectionAttemptStatus.refresh)
  const verificationDiagnosisStatus = useVerificationDiagnosisStatus(runId, cockpit?.latestSequence, evidenceRefreshGeneration)
  // Both identifiers come from the host's own diagnosis status, never from the timeline: the report whose
  // current verification can be diagnosed now, and the diagnosis whose findings can be corrected.
  const diagnosableExecutionReportMessageId =
    verificationDiagnosisStatus.status?.diagnosableExecutionReportMessageId ?? null
  const verificationDiagnosisAttemptId = verificationDiagnosisStatus.status?.hasAttempt
    ? (verificationDiagnosisStatus.status.attemptId ?? null)
    : null
  const requestVerificationDiagnosis = useRequestVerificationDiagnosis(
    runId,
    diagnosableExecutionReportMessageId,
    verificationDiagnosisStatus.refresh,
    evidenceRefreshGeneration,
  )
  const requestDiagnosisCorrection = useRequestDiagnosisCorrection(
    runId,
    verificationDiagnosisAttemptId,
    verificationDiagnosisStatus.refresh,
  )
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
  // A diagnosis-origin correction that applied and is still current names the corrected report itself, which
  // the ordinary code review then targets; otherwise the ordinary review-correction logic is unchanged.
  // A failed diagnosis read is not validation of an older fallback report: the review-correction report is only the target when
  // the diagnosis status was actually read and named none, so a diagnosis read error leaves no target at all.
  const latestExecutionReportMessageId = verificationDiagnosisStatus.error
    ? null
    : (verificationDiagnosisStatus.status?.reviewableExecutionReportMessageId ??
      selectLatestExecutionReportMessageId(
        reviewCorrectionAttemptStatus.status?.reviewableExecutionReportMessageId,
        reviewCorrectionAttemptStatus.loading,
        reviewCorrectionAttemptStatus.error,
      ))
  // The ordinary review needs both reads: a failed code-review read is not "no previous review", and neither read pending or failed
  // may leave the request available. The server stays authoritative for the eligibility itself.
  const ordinaryReviewReadsSettled =
    !codeReviewAttemptStatus.loading && !codeReviewAttemptStatus.error && !verificationDiagnosisStatus.loading && !verificationDiagnosisStatus.error
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
  // The explicit human decision on the final plan of a completed second challenge round (ADR-0016). The facts are the
  // server's read of the escalation the loaded timeline shows; a plan is offered for implementation only when the server
  // names that exact final revision with an Available (or already Consumed, to keep showing its claim) authorization.
  // Provider review and resolution of the final revision stay unavailable whatever this says.
  const planningEscalationId =
    planningLineage.depth === 2 && !planningLineage.ambiguous && planningLineage.secondRevision
      ? (planningLineage.escalation?.id ?? null)
      : null
  const planningAuthorization = usePlanningImplementationAuthorization(runId, planningEscalationId, cockpit?.latestSequence)
  const authorizePlanning = useAuthorizePlanningImplementation(runId, planningEscalationId ?? '', planningAuthorization.refresh)
  const authorizedPlanMessageId = selectAuthorizedPlanMessageId(planningLineage, planningAuthorization.authorization)
  const refreshImplementationStatus = implementationAttemptStatus.refresh
  const refreshAuthorization = planningAuthorization.refresh
  const refreshAfterImplementationRequest = useCallback(() => {
    refreshImplementationStatus()
    refreshAuthorization()
  }, [refreshImplementationStatus, refreshAuthorization])
  const implementablePlanMessageId = eligiblePlanProposalMessageId ?? authorizedPlanMessageId
  const requestImplementation = useRequestImplementation(runId, implementablePlanMessageId, refreshAfterImplementationRequest)

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
  // The Agent request actions are offered only when this run's durable execution mode admits Agent
  // work: a simulated run and an unrecognized or missing mode withhold them (the backend refuses the
  // same requests independently). A projection still held from another run is not this run's mode;
  // that frame is already fail-closed through the claim-block derivation below.
  const executionMode = deriveRunExecutionModeDisclosure(cockpit.executionMode, cockpit.lifecycle)
  const agentActionsAllowed = cockpit.runId !== runId || executionMode.agentActionsAllowed
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
      {cockpit.runId === runId && <RunExecutionModeNotice executionMode={cockpit.executionMode} lifecycle={cockpit.lifecycle} />}
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
          {agentActionsAllowed ? (
          <>
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
          {planningEscalationId && planningLineage.secondRevision && (
            <PlanningImplementationAuthorizationPanel
              runId={runId}
              escalationMessageId={planningEscalationId}
              timelineFinalProposalMessageId={planningLineage.secondRevision.id}
              authorization={planningAuthorization.authorization}
              loading={planningAuthorization.loading}
              readError={planningAuthorization.error}
              authorizing={authorizePlanning.authorizing}
              authorizeError={authorizePlanning.error}
              onAuthorize={(rationale) => authorizePlanning.authorize(runId, planningEscalationId, rationale)}
            />
          )}
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
            runId={runId}
            planProposalMessageId={implementablePlanMessageId}
            status={implementationAttemptStatus.status}
            statusLoading={implementationAttemptStatus.loading}
            statusError={implementationAttemptStatus.error}
            requesting={requestImplementation.requesting}
            requestError={requestImplementation.error}
            onRequest={(guidance) =>
              implementablePlanMessageId
                ? requestImplementation.request(runId, implementablePlanMessageId, guidance)
                : Promise.resolve(false)
            }
            globalClaimBlock={globalClaimBlock}
            timeFit={implementationTimeFit}
            requestLabel={authorizedPlanMessageId ? AUTHORIZED_REQUEST_LABEL : undefined}
            requestWithheldNote={
              authorizedPlanMessageId && planningAuthorization.authorization?.state === 'Consumed'
                ? AUTHORIZATION_SPENT_NOTE
                : null
            }
          />
          <CodeReviewAction
            executionReportMessageId={latestExecutionReportMessageId}
            status={codeReviewAttemptStatus.status}
            statusLoading={codeReviewAttemptStatus.loading || verificationDiagnosisStatus.loading}
            statusError={codeReviewAttemptStatus.error}
            requesting={requestCodeReview.requesting || requestCodeReviewRepair.requesting}
            requestError={requestCodeReview.error}
            onRequest={() =>
              latestExecutionReportMessageId &&
              ordinaryReviewReadsSettled &&
              void requestCodeReview.request(runId, latestExecutionReportMessageId)
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
            runId={runId}
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
            onRequest={(guidance) => {
              const reviewAttemptId = codeReviewAttemptStatus.status?.attemptId
              return reviewAttemptId ? requestReviewCorrection.request(runId, reviewAttemptId, guidance) : Promise.resolve(false)
            }}
          />
          <VerificationDiagnosisAction
            runId={runId}
            status={verificationDiagnosisStatus.status}
            statusLoading={verificationDiagnosisStatus.loading}
            statusError={verificationDiagnosisStatus.error}
            requesting={requestVerificationDiagnosis.requesting}
            requestError={requestVerificationDiagnosis.error}
            onRequest={() =>
              diagnosableExecutionReportMessageId &&
              void requestVerificationDiagnosis.request(runId, diagnosableExecutionReportMessageId)
            }
            correctionRequesting={requestDiagnosisCorrection.requesting}
            correctionError={requestDiagnosisCorrection.error}
            onRequestCorrection={() =>
              verificationDiagnosisAttemptId && void requestDiagnosisCorrection.request(runId, verificationDiagnosisAttemptId)
            }
            onRequestCorrectionWithGuidance={(guidance) =>
              verificationDiagnosisAttemptId
                ? requestDiagnosisCorrection.request(runId, verificationDiagnosisAttemptId, guidance)
                : Promise.resolve(false)
            }
            globalClaimBlock={globalClaimBlock}
            timeFit={codeReviewTimeFit}
            correctionTimeFit={reviewCorrectionTimeFit}
          />
          </>
          ) : (
            <p className="dc-run-agent-actions-note">
              {executionMode.agentActionsNote}
            </p>
          )}
          <AgentCollaboration runId={runId} {...collaborationTimeline} />
        </div>
        <UsageEvidenceRail runId={runId} />
      </div>
      <LiveOutputDrawer runId={runId} attemptId={currentProcessAttemptId} />
    </>
  )
}
