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
  GetRunCockpitEndpointClient,
  GetCollaborationTimelineEndpointClient,
  GetRunEventsEndpointClient,
  PrepareRepositoryWorkspaceEndpointClient,
  RecheckProjectPhysicalIdentityEndpointClient,
  RegisterProjectEndpointClient,
  RequestHostCapabilityRefreshEndpointClient,
  StartSimulatedRunEndpointClient,
  RequestCodexPlanningAttemptEndpointClient,
  GetAgentAttemptStatusEndpointClient,
  RequestClaudeCriticalReviewEndpointClient,
  GetClaudeCriticalReviewAttemptStatusEndpointClient,
  RequestChallengeResolutionEndpointClient,
  GetChallengeResolutionAttemptStatusEndpointClient,
  RequestImplementationEndpointClient,
  GetImplementationAttemptStatusEndpointClient,
  RequestCodeReviewEndpointClient,
  GetCodeReviewAttemptStatusEndpointClient,
  RequestReviewCorrectionEndpointClient,
  GetReviewCorrectionAttemptStatusEndpointClient,
  AuthorizeReviewCorrectionEndpointClient,
} from './generated/api-client'
import { authenticatedHttp, getApiBaseUrl } from './httpClient'

// One instance per endpoint client is enough for this slice; components import the
// factory functions rather than constructing a client or a URL themselves.
export const projectsClient = () => new GetProjectRunSummariesEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const registerProjectClient = () => new RegisterProjectEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const startSimulatedRunClient = () => new StartSimulatedRunEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const runCockpitClient = () => new GetRunCockpitEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const collaborationTimelineClient = () => new GetCollaborationTimelineEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const runEventsClient = () => new GetRunEventsEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const environmentClient = () => new RequestHostCapabilityRefreshEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const providerRuntimePreflightClient = () => new GetProviderRuntimePreflightEndpointClient(getApiBaseUrl(), authenticatedHttp)
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
export const agentAttemptStatusClient = () => new GetAgentAttemptStatusEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const requestClaudeCriticalReviewClient = () => new RequestClaudeCriticalReviewEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const claudeCriticalReviewAttemptStatusClient = () => new GetClaudeCriticalReviewAttemptStatusEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const requestChallengeResolutionClient = () => new RequestChallengeResolutionEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const challengeResolutionAttemptStatusClient = () => new GetChallengeResolutionAttemptStatusEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const requestImplementationClient = () => new RequestImplementationEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const implementationAttemptStatusClient = () => new GetImplementationAttemptStatusEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const requestCodeReviewClient = () => new RequestCodeReviewEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const codeReviewAttemptStatusClient = () => new GetCodeReviewAttemptStatusEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const requestReviewCorrectionClient = () => new RequestReviewCorrectionEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const reviewCorrectionAttemptStatusClient = () => new GetReviewCorrectionAttemptStatusEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const authorizeReviewCorrectionClient = () => new AuthorizeReviewCorrectionEndpointClient(getApiBaseUrl(), authenticatedHttp)

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
  RecheckProjectPhysicalIdentityResponse,
  RegisterProjectResponse,
  RequestHostCapabilityRefreshResponse,
  RunEventResponse,
  CollaborationMessageTimelineResponse,
  StageMapEntryResponse,
  UpdateVerificationCommandRequest,
  VerificationCommandResponse,
  VerificationExecutionResponse,
  CheckpointReviewResponse,
  RequestCodexPlanningAttemptResponse,
  AgentAttemptStatusResponse,
  AgentAttemptArtifactMetadataResponse,
  RequestClaudeCriticalReviewResponse,
  ClaudeCriticalReviewAttemptStatusResponse,
  RequestChallengeResolutionResponse,
  ChallengeResolutionAttemptStatusResponse,
  RequestImplementationResponse,
  ImplementationAttemptStatusResponse,
  RequestCodeReviewResponse,
  CodeReviewAttemptStatusResponse,
  RequestReviewCorrectionResponse,
  ReviewCorrectionAttemptStatusResponse,
  AuthorizeReviewCorrectionResponse,
  AgentProcessExecutionResponse,
  RunCockpitAgentAttemptResponse,
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
} from './generated/api-client'
