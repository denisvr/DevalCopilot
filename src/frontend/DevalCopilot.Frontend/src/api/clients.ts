import {
  GetProcessAttemptOutputEndpointClient,
  ClaimVerificationExecutionEndpointClient,
  RecordCheckpointReviewEndpointClient,
  CaptureGitWorkspaceCheckpointEndpointClient,
  GetGitCheckpointChangedFilesEndpointClient,
  GetGitCheckpointDiffEndpointClient,
  GetProjectGitEvidenceEndpointClient,
  ConfigureVerificationCommandEndpointClient,
  DeleteVerificationCommandEndpointClient,
  GetVerificationExecutionOutputEndpointClient,
  GetProjectVerificationCommandsEndpointClient,
  GetProjectVerificationExecutionsEndpointClient,
  GetProjectCheckpointReviewsEndpointClient,
  GetCheckpointApprovalEvidenceEndpointClient,
  UpdateVerificationCommandEndpointClient,
  GetProjectRunSummariesEndpointClient,
  GetProjectRunHistoryEndpointClient,
  GetRunEventHistoryEndpointClient,
  GetCollaborationHistoryEndpointClient,
  GetCollaborationHistoryReplyEndpointClient,
  GetCollaborationHistoryAttemptEndpointClient,
  GetProjectWorkspaceEndpointClient,
  GetProviderRuntimePreflightEndpointClient,
  GetCodexAccountAllowanceEndpointClient,
  GetCodexModelCatalogEndpointClient,
  GetGitHubStoredAuthenticationEndpointClient,
  GetProjectGitHubOriginEndpointClient,
  SetCodexAssignmentPreferenceEndpointClient,
  SetClaudeModelPreferenceEndpointClient,
  SetClaudeMutationTurnLimitEndpointClient,
  SetCodexAccountUsageStopEndpointClient,
  SetCodexAccountUsageWarningEndpointClient,
  GetCodexAccountUsageWarningEndpointClient,
  SetTokenWarningThresholdEndpointClient,
  SetTokenStopThresholdEndpointClient,
  GetRunCockpitEndpointClient,
  GetCollaborationTimelineEndpointClient,
  GetCollaborationMessageEvidenceEndpointClient,
  GetSealedAgentArtifactWindowEndpointClient,
  GetAgentAttemptHistoryEndpointClient,
  GetAgentAttemptArtifactWindowEndpointClient,
  GetRunEventsEndpointClient,
  PrepareRepositoryWorkspaceEndpointClient,
  RecheckProjectPhysicalIdentityEndpointClient,
  RegisterProjectEndpointClient,
  RequestHostCapabilityRefreshEndpointClient,
  StartSimulatedRunEndpointClient,
  CreateManualRunEndpointClient,
  RequestCodexPlanningAttemptEndpointClient,
  RequestCodexPlanningRepairAttemptEndpointClient,
  GetAgentAttemptStatusEndpointClient,
  RequestClaudeCriticalReviewEndpointClient,
  RequestClaudeCriticalReviewRepairAttemptEndpointClient,
  GetClaudeCriticalReviewAttemptStatusEndpointClient,
  RequestChallengeResolutionEndpointClient,
  RequestChallengeResolutionRepairAttemptEndpointClient,
  GetChallengeResolutionAttemptStatusEndpointClient,
  RequestImplementationEndpointClient,
  GetImplementationAttemptStatusEndpointClient,
  RequestCodeReviewEndpointClient,
  RequestCodeReviewRepairAttemptEndpointClient,
  GetCodeReviewAttemptStatusEndpointClient,
  RequestReviewCorrectionEndpointClient,
  GetReviewCorrectionAttemptStatusEndpointClient,
  AuthorizeReviewCorrectionEndpointClient,
  AuthorizeReviewCorrectionWithGuidanceEndpointClient,
  RequestVerificationDiagnosisEndpointClient,
  GetVerificationDiagnosisStatusEndpointClient,
  RequestDiagnosisCorrectionEndpointClient,
  AuthorizePlanningImplementationEndpointClient,
  GetPlanningImplementationAuthorizationEndpointClient,
  RequestLocalCommitEndpointClient,
  GetLocalCommitStatusEndpointClient,
  GetLocalDeliveryReceiptEndpointClient,
  AbandonManualRunEndpointClient,
  GetManualRunAbandonmentEndpointClient,
  RecordGitHubObservationTargetEndpointClient,
  GetGitHubObservationTargetEndpointClient,
  GetGitHubRepositoryObservationEndpointClient,
  GetGitHubBranchObservationEndpointClient,
  GetGitHubCiObservationEndpointClient,
  GetGitHubRepositoryPermissionObservationEndpointClient,
  GetGitHubCheckContextsObservationEndpointClient,
  GetGitHubCheckRunOutputObservationEndpointClient,
  BindGitHubRepositoryIdentityEndpointClient,
  GetGitHubRepositoryIdentityBindingEndpointClient,
  RecordGitHubBranchPublicationApprovalEndpointClient,
  GetGitHubBranchPublicationApprovalEndpointClient,
} from './generated/api-client'
import { authenticatedHttp, getApiBaseUrl } from './httpClient'
import { AgentAttemptEvidenceWireClient } from '../features/cockpit/agentAttemptEvidenceWire'
import { AgentAttemptInputsWireClient } from '../features/cockpit/agentAttemptInputsWire'
import { AgentAttemptInputMessageWireClient } from '../features/cockpit/agentAttemptInputMessageWire'
import { AgentAttemptOutputsWireClient } from '../features/cockpit/agentAttemptOutputsWire'
import { AgentAttemptOutputMessageWireClient } from '../features/cockpit/agentAttemptOutputMessageWire'
import { AgentSupervisionWireClient } from '../features/cockpit/agentSupervisionWire'
import { WorkspaceExecutionWireClient } from '../features/cockpit/workspaceExecutionWire'
import { ExecutionCapacityLimitWireClient, ExecutionCapacityWireClient } from '../features/cockpit/executionCapacityWire'
import {
  ArmPlanningSequenceWireClient,
  PlanningSequenceStatusWireClient,
  StopPlanningSequenceWireClient,
} from '../features/cockpit/planningSequenceWire'
import {
  ArmLocalExecutionSequenceWireClient,
  LocalExecutionSequenceAvailabilityWireClient,
  LocalExecutionSequenceStatusWireClient,
  StopLocalExecutionSequenceWireClient,
} from '../features/cockpit/localExecutionSequenceWire'
import {
  AgentAttemptVerificationEvidenceWireClient,
  AgentAttemptVerificationWindowWireClient,
} from '../features/cockpit/agentAttemptVerificationWire'

// One instance per endpoint client is enough for this slice; components import the
// factory functions rather than constructing a client or a URL themselves.
export const projectsClient = () => new GetProjectRunSummariesEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const projectRunHistoryClient = () => new GetProjectRunHistoryEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const runEventHistoryClient = () => new GetRunEventHistoryEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const collaborationHistoryClient = () => new GetCollaborationHistoryEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const collaborationReplyClient = () => new GetCollaborationHistoryReplyEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const collaborationAttemptClient = () => new GetCollaborationHistoryAttemptEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const registerProjectClient =() => new RegisterProjectEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const startSimulatedRunClient = () => new StartSimulatedRunEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const createManualRunClient = () => new CreateManualRunEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const runCockpitClient = () => new GetRunCockpitEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const collaborationTimelineClient = () => new GetCollaborationTimelineEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const collaborationMessageEvidenceClient = () => new GetCollaborationMessageEvidenceEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const sealedAgentArtifactWindowClient = () => new GetSealedAgentArtifactWindowEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const agentAttemptHistoryClient = () => new GetAgentAttemptHistoryEndpointClient(getApiBaseUrl(), authenticatedHttp)
// Judges the raw wire timestamps before the generated client revives them (see AgentAttemptEvidenceWireClient).
export const agentAttemptEvidenceClient = () => new AgentAttemptEvidenceWireClient(getApiBaseUrl(), authenticatedHttp)
// Hands the parsed body to the whole-answer decoder instead of the generated model (see AgentAttemptInputsWireClient).
export const agentAttemptInputsClient = () => new AgentAttemptInputsWireClient(getApiBaseUrl(), authenticatedHttp)
// Hands the parsed body to the whole-answer decoder of one selected input message (see AgentAttemptInputMessageWireClient).
export const agentAttemptInputMessageClient = () => new AgentAttemptInputMessageWireClient(getApiBaseUrl(), authenticatedHttp)
// Hand the parsed bodies to the whole-answer decoders of the recorded outputs and one selected output message (see the ADR-0054 wire clients).
export const agentAttemptOutputsClient = () => new AgentAttemptOutputsWireClient(getApiBaseUrl(), authenticatedHttp)
export const agentAttemptOutputMessageClient = () => new AgentAttemptOutputMessageWireClient(getApiBaseUrl(), authenticatedHttp)
// Hands the parsed body to the whole-answer decoder of the current Agent supervision read (ADR-0056); its own signal cancels the request.
export const agentSupervisionClient = (signal?: AbortSignal) => new AgentSupervisionWireClient(getApiBaseUrl(), authenticatedHttp, signal)
// Hands the parsed body to the whole-answer decoder of the workspace execution status of one project (ADR-0057); its own signal cancels the request.
export const workspaceExecutionStatusClient = (signal?: AbortSignal) => new WorkspaceExecutionWireClient(getApiBaseUrl(), authenticatedHttp, signal)
// Hands the parsed body to the whole-answer decoder of the host-wide execution capacity (ADR-0058); its own signal cancels the request.
export const executionCapacityClient = (signal?: AbortSignal) => new ExecutionCapacityWireClient(getApiBaseUrl(), authenticatedHttp, signal)
// The one explicit limit change (ADR-0059): the same parsed-body facade, whose own signal cancels the actual request and which never retries.
export const executionCapacityLimitClient = (signal?: AbortSignal) => new ExecutionCapacityLimitWireClient(getApiBaseUrl(), authenticatedHttp, signal)
// Hand the parsed bodies to the whole-answer decoders of the planning-sequence status, arming receipt and Stop answer (ADR-0055).
export const planningSequenceStatusClient = () => new PlanningSequenceStatusWireClient(getApiBaseUrl(), authenticatedHttp)
export const armPlanningSequenceClient = () => new ArmPlanningSequenceWireClient(getApiBaseUrl(), authenticatedHttp)
export const stopPlanningSequenceClient = () => new StopPlanningSequenceWireClient(getApiBaseUrl(), authenticatedHttp)
// The local-execution-sequence operations (ADR-0060): parsed-body facades; each carries its own signal that cancels the actual request.
export const localExecutionSequenceStatusClient = (signal?: AbortSignal) => new LocalExecutionSequenceStatusWireClient(getApiBaseUrl(), authenticatedHttp, signal)
export const localExecutionSequenceAvailabilityClient = (signal?: AbortSignal) => new LocalExecutionSequenceAvailabilityWireClient(getApiBaseUrl(), authenticatedHttp, signal)
export const armLocalExecutionSequenceClient = (signal?: AbortSignal) => new ArmLocalExecutionSequenceWireClient(getApiBaseUrl(), authenticatedHttp, signal)
export const stopLocalExecutionSequenceClient = (signal?: AbortSignal) => new StopLocalExecutionSequenceWireClient(getApiBaseUrl(), authenticatedHttp, signal)
// Hand the parsed bodies to the whole-answer decoders instead of the generated models (see the ADR-0052 wire clients).
export const agentAttemptVerificationEvidenceClient = () =>
  new AgentAttemptVerificationEvidenceWireClient(getApiBaseUrl(), authenticatedHttp)
export const agentAttemptVerificationWindowClient = () => new AgentAttemptVerificationWindowWireClient(getApiBaseUrl(), authenticatedHttp)
export const agentAttemptArtifactWindowClient =() => new GetAgentAttemptArtifactWindowEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const runEventsClient = () => new GetRunEventsEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const environmentClient = () => new RequestHostCapabilityRefreshEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const providerRuntimePreflightClient = () => new GetProviderRuntimePreflightEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const codexAccountAllowanceClient = () => new GetCodexAccountAllowanceEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const codexModelCatalogClient = () => new GetCodexModelCatalogEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const gitHubStoredAuthenticationClient = () => new GetGitHubStoredAuthenticationEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const projectGitHubOriginClient = () => new GetProjectGitHubOriginEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const setCodexAssignmentPreferenceClient = () => new SetCodexAssignmentPreferenceEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const setClaudeModelPreferenceClient = () => new SetClaudeModelPreferenceEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const setClaudeMutationTurnLimitClient = () => new SetClaudeMutationTurnLimitEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const setCodexAccountUsageStopClient = () => new SetCodexAccountUsageStopEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const setCodexAccountUsageWarningClient = () => new SetCodexAccountUsageWarningEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const getCodexAccountUsageWarningClient = () => new GetCodexAccountUsageWarningEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const setTokenWarningThresholdClient = () => new SetTokenWarningThresholdEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const setTokenStopThresholdClient = () => new SetTokenStopThresholdEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const processAttemptOutputClient = () => new GetProcessAttemptOutputEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const projectWorkspaceClient = () => new GetProjectWorkspaceEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const prepareWorkspaceClient = () => new PrepareRepositoryWorkspaceEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const recheckPhysicalIdentityClient = () => new RecheckProjectPhysicalIdentityEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const projectGitEvidenceClient = () => new GetProjectGitEvidenceEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const captureGitWorkspaceCheckpointClient = () => new CaptureGitWorkspaceCheckpointEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const gitCheckpointChangedFilesClient = () => new GetGitCheckpointChangedFilesEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const gitCheckpointDiffClient = () => new GetGitCheckpointDiffEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const projectVerificationCommandsClient = () => new GetProjectVerificationCommandsEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const projectVerificationExecutionsClient = () => new GetProjectVerificationExecutionsEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const projectCheckpointReviewsClient = () => new GetProjectCheckpointReviewsEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const checkpointApprovalEvidenceClient = () => new GetCheckpointApprovalEvidenceEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const configureVerificationCommandClient = () => new ConfigureVerificationCommandEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const updateVerificationCommandClient = () => new UpdateVerificationCommandEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const deleteVerificationCommandClient = () => new DeleteVerificationCommandEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const claimVerificationExecutionClient = () => new ClaimVerificationExecutionEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const recordCheckpointReviewClient = () => new RecordCheckpointReviewEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const verificationExecutionOutputClient = () => new GetVerificationExecutionOutputEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const requestCodexPlanningAttemptClient = () => new RequestCodexPlanningAttemptEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const requestCodexPlanningRepairAttemptClient = () => new RequestCodexPlanningRepairAttemptEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const agentAttemptStatusClient = () => new GetAgentAttemptStatusEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const requestClaudeCriticalReviewClient = () => new RequestClaudeCriticalReviewEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const requestClaudeCriticalReviewRepairAttemptClient = () => new RequestClaudeCriticalReviewRepairAttemptEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const claudeCriticalReviewAttemptStatusClient = () => new GetClaudeCriticalReviewAttemptStatusEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const requestChallengeResolutionClient = () => new RequestChallengeResolutionEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const requestChallengeResolutionRepairAttemptClient = () => new RequestChallengeResolutionRepairAttemptEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const challengeResolutionAttemptStatusClient = () => new GetChallengeResolutionAttemptStatusEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const requestImplementationClient = () => new RequestImplementationEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const implementationAttemptStatusClient = () => new GetImplementationAttemptStatusEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const requestCodeReviewClient = () => new RequestCodeReviewEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const requestCodeReviewRepairAttemptClient = () => new RequestCodeReviewRepairAttemptEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const codeReviewAttemptStatusClient = () => new GetCodeReviewAttemptStatusEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const requestReviewCorrectionClient = () => new RequestReviewCorrectionEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const reviewCorrectionAttemptStatusClient = () => new GetReviewCorrectionAttemptStatusEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const authorizeReviewCorrectionClient = () => new AuthorizeReviewCorrectionEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const authorizeReviewCorrectionWithGuidanceClient = () => new AuthorizeReviewCorrectionWithGuidanceEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const requestVerificationDiagnosisClient = () => new RequestVerificationDiagnosisEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const verificationDiagnosisStatusClient = () => new GetVerificationDiagnosisStatusEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const requestDiagnosisCorrectionClient = () => new RequestDiagnosisCorrectionEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const authorizePlanningImplementationClient = () => new AuthorizePlanningImplementationEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const planningImplementationAuthorizationClient = () => new GetPlanningImplementationAuthorizationEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const requestLocalCommitClient = () => new RequestLocalCommitEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const localCommitStatusClient = () => new GetLocalCommitStatusEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const localDeliveryReceiptClient = () => new GetLocalDeliveryReceiptEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const abandonManualRunClient = () => new AbandonManualRunEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const manualRunAbandonmentClient = () => new GetManualRunAbandonmentEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const recordGitHubObservationTargetClient = () => new RecordGitHubObservationTargetEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const gitHubObservationTargetClient = () => new GetGitHubObservationTargetEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const gitHubRepositoryObservationClient = () => new GetGitHubRepositoryObservationEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const gitHubBranchObservationClient = () => new GetGitHubBranchObservationEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const gitHubCiObservationClient = () => new GetGitHubCiObservationEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const gitHubRepositoryPermissionObservationClient = () =>
  new GetGitHubRepositoryPermissionObservationEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const gitHubCheckContextsObservationClient = () =>
  new GetGitHubCheckContextsObservationEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const gitHubCheckRunOutputObservationClient = () =>
  new GetGitHubCheckRunOutputObservationEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const bindGitHubRepositoryIdentityClient = () => new BindGitHubRepositoryIdentityEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const gitHubRepositoryIdentityBindingClient = () =>
  new GetGitHubRepositoryIdentityBindingEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const recordGitHubBranchPublicationApprovalClient = () =>
  new RecordGitHubBranchPublicationApprovalEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const gitHubBranchPublicationApprovalClient = () =>
  new GetGitHubBranchPublicationApprovalEndpointClient(getApiBaseUrl(), authenticatedHttp)

export type {
  CapabilityReadinessResponse,
  ConfigureVerificationCommandRequest,
  ConfigureVerificationCommandResponse,
  CaptureGitWorkspaceCheckpointResponse,
  GetGitCheckpointDiffResponse,
  GetProjectGitEvidenceResponse,
  GitCheckpointChangedFileResponse,
  GetProcessAttemptOutputResponse,
  GetProjectWorkspaceResponse,
  GetRunCockpitResponse,
  ParticipantIdentityResponse,
  PrepareRepositoryWorkspaceResponse,
  ProjectRunSummaryResponse,
  ProviderRuntimePreflightResponse,
  CodexAccountAllowanceResponse,
  CodexAllowanceBucketResponse,
  CodexAllowanceWindowResponse,
  CodexModelCatalogResponse,
  CodexModelCatalogEntryResponse,
  GitHubStoredAuthenticationResponse,
  ProjectGitHubOriginResponse,
  GitHubOriginTargetResponse,
  SetCodexAssignmentPreferenceResponse,
  SetClaudeModelPreferenceResponse,
  SetClaudeMutationTurnLimitResponse,
  ClaudeMutationTurnLimitResponse,
  CodexAccountUsageStopResponse,
  CodexAccountUsageDecisionResponse,
  CodexAccountUsageWindowResponse,
  CodexAccountUsageWarningSettingResponse,
  GetCodexAccountUsageWarningResponse,
  CodexAccountUsageWarningWindowResponse,
  DirectHumanGuidanceResponse,
  SetTokenWarningThresholdResponse,
  RunCockpitTokenWarningResponse,
  SetTokenStopThresholdResponse,
  RunCockpitTokenStopResponse,
  RecheckProjectPhysicalIdentityResponse,
  RegisterProjectResponse,
  RequestHostCapabilityRefreshResponse,
  RunEventResponse,
  CollaborationMessageTimelineResponse,
  CollaborationMessageEvidenceResponse,
  SealedAgentArtifactWindowResponse,
  AgentAttemptHistoryResponse,
  AgentAttemptHistoryEntryResponse,
  AgentAttemptEvidenceResponse,
  AgentModelContextLimitsResponse,
  AgentModelContextLimitResponse,
  StageMapEntryResponse,
  UpdateVerificationCommandRequest,
  VerificationCommandResponse,
  VerificationExecutionResponse,
  CheckpointReviewResponse,
  CheckpointApprovalEvidenceResponse,
  CheckpointApprovalEvidenceMemberResponse,
  RequestCodexPlanningAttemptResponse,
  RequestCodexPlanningRepairAttemptResponse,
  AgentAttemptStatusResponse,
  AgentAttemptArtifactMetadataResponse,
  RequestClaudeCriticalReviewResponse,
  RequestClaudeCriticalReviewRepairAttemptResponse,
  ClaudeCriticalReviewAttemptStatusResponse,
  RequestChallengeResolutionResponse,
  RequestChallengeResolutionRepairAttemptResponse,
  ChallengeResolutionAttemptStatusResponse,
  RequestImplementationResponse,
  ImplementationAttemptStatusResponse,
  RequestCodeReviewResponse,
  RequestCodeReviewRepairAttemptResponse,
  CodeReviewAttemptStatusResponse,
  RequestReviewCorrectionResponse,
  ReviewCorrectionAttemptStatusResponse,
  VerificationDiagnosisStatusResponse,
  VerificationDiagnosisMemberResponse,
  RequestVerificationDiagnosisResponse,
  RequestDiagnosisCorrectionResponse,
  AuthorizeReviewCorrectionResponse,
  AuthorizePlanningImplementationResponse,
  PlanningImplementationAuthorizationResponse,
  AgentProcessExecutionResponse,
  AgentTokenUsageResponse,
  RunCockpitAgentAttemptResponse,
  RunTokenUsageSummaryResponse,
  AgentClaimPathTimeFitResponse,
  LocalCommitOperationResponse,
  GetLocalCommitStatusResponse,
  GetLocalDeliveryReceiptResponse,
  GetProjectRunHistoryResponse,
  GetRunEventHistoryResponse,
  GetCollaborationHistoryResponse,
  GetCollaborationHistoryReplyResponse,
  GetCollaborationHistoryAttemptResponse,
  LocalDeliveryReceiptResponse,
  AbandonManualRunResponse,
  GetManualRunAbandonmentResponse,
  ManualRunAbandonmentResponse,
  GetGitHubObservationTargetResponse,
  GetGitHubRepositoryObservationResponse,
  GitHubRepositoryIdentityResponse,
  GetGitHubBranchObservationResponse,
  BindGitHubRepositoryIdentityResponse,
  GetGitHubRepositoryIdentityBindingResponse,
  GitHubRepositoryIdentityBindingResponse,
  RecordGitHubBranchPublicationApprovalResponse,
  GetGitHubBranchPublicationApprovalResponse,
  GitHubBranchPublicationApprovalSourceResponse,
  GitHubBranchPublicationApprovalTargetResponse,
  GitHubBranchPublicationApprovalBindingResponse,
  GitHubBranchPublicationApprovalResponse,
  GitHubBranchObservationResponse,
  GitHubBranchRefResponse,
  GetGitHubRepositoryPermissionObservationResponse,
  GitHubRepositoryPermissionObservationResponse,
  GetGitHubCiObservationResponse,
  GitHubCiObservationResponse,
  GitHubCiRefResponse,
  GetGitHubCheckContextsObservationResponse,
  GitHubCheckContextsObservationResponse,
  GitHubCheckContextsRefResponse,
  GitHubCheckContextResponse,
  GetGitHubCheckRunOutputObservationResponse,
  GitHubCheckRunOutputObservationResponse,
  GitHubCheckRunOutputRefResponse,
  GitHubCheckRunOutputCheckRunResponse,
  GitHubCheckRunOutputTextResponse,
  RecordGitHubObservationTargetResponse,
} from './generated/api-client'
export {
  ClaimVerificationExecutionRequest,
  RecordCheckpointReviewRequest,
  RegisterProjectRequest,
  RequestClaudeCriticalReviewRequest,
  RequestChallengeResolutionRequest,
  RequestImplementationRequest,
  RequestCodeReviewRequest,
  RequestReviewCorrectionRequest,
  RequestVerificationDiagnosisRequest,
  RequestDiagnosisCorrectionRequest,
  SetCodexAssignmentPreferenceRequest,
  RequestLocalCommitRequest,
  AbandonManualRunRequest,
  ArmPlanningSequenceRequest,
  RecordGitHubObservationTargetRequest,
  BindGitHubRepositoryIdentityRequest,
  RecordGitHubBranchPublicationApprovalRequest,
} from './generated/api-client'
