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
  UpdateVerificationCommandEndpointClient,
  GetProjectRunSummariesEndpointClient,
  GetProjectWorkspaceEndpointClient,
  GetProviderRuntimePreflightEndpointClient,
  GetCodexAccountAllowanceEndpointClient,
  GetCodexModelCatalogEndpointClient,
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
  GetAgentAttemptEvidenceEndpointClient,
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
} from './generated/api-client'
import { authenticatedHttp, getApiBaseUrl } from './httpClient'

// One instance per endpoint client is enough for this slice; components import the
// factory functions rather than constructing a client or a URL themselves.
export const projectsClient = () => new GetProjectRunSummariesEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const registerProjectClient = () => new RegisterProjectEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const startSimulatedRunClient = () => new StartSimulatedRunEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const createManualRunClient = () => new CreateManualRunEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const runCockpitClient = () => new GetRunCockpitEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const collaborationTimelineClient = () => new GetCollaborationTimelineEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const collaborationMessageEvidenceClient = () => new GetCollaborationMessageEvidenceEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const sealedAgentArtifactWindowClient = () => new GetSealedAgentArtifactWindowEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const agentAttemptHistoryClient = () => new GetAgentAttemptHistoryEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const agentAttemptEvidenceClient = () => new GetAgentAttemptEvidenceEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const agentAttemptArtifactWindowClient = () => new GetAgentAttemptArtifactWindowEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const runEventsClient = () => new GetRunEventsEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const environmentClient = () => new RequestHostCapabilityRefreshEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const providerRuntimePreflightClient = () => new GetProviderRuntimePreflightEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const codexAccountAllowanceClient = () => new GetCodexAccountAllowanceEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const codexModelCatalogClient = () => new GetCodexModelCatalogEndpointClient(getApiBaseUrl(), authenticatedHttp)
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
} from './generated/api-client'
