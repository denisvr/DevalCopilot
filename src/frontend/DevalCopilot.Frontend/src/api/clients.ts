import {
  GetProcessAttemptOutputEndpointClient,
  CaptureGitWorkspaceCheckpointEndpointClient,
  GetGitCheckpointChangedFilesEndpointClient,
  GetGitCheckpointDiffEndpointClient,
  GetProjectGitEvidenceEndpointClient,
  ConfigureVerificationCommandEndpointClient,
  DeleteVerificationCommandEndpointClient,
  GetProjectVerificationCommandsEndpointClient,
  UpdateVerificationCommandEndpointClient,
  GetProjectRunSummariesEndpointClient,
  GetProjectWorkspaceEndpointClient,
  GetRunCockpitEndpointClient,
  GetRunEventsEndpointClient,
  PrepareRepositoryWorkspaceEndpointClient,
  RecheckProjectPhysicalIdentityEndpointClient,
  RegisterProjectEndpointClient,
  RequestHostCapabilityRefreshEndpointClient,
  StartSimulatedRunEndpointClient,
} from './generated/api-client'
import { authenticatedHttp, getApiBaseUrl } from './httpClient'

// One instance per endpoint client is enough for this slice; components import the
// factory functions rather than constructing a client or a URL themselves.
export const projectsClient = () => new GetProjectRunSummariesEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const registerProjectClient = () => new RegisterProjectEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const startSimulatedRunClient = () => new StartSimulatedRunEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const runCockpitClient = () => new GetRunCockpitEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const runEventsClient = () => new GetRunEventsEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const environmentClient = () => new RequestHostCapabilityRefreshEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const processAttemptOutputClient = () => new GetProcessAttemptOutputEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const projectWorkspaceClient = () => new GetProjectWorkspaceEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const prepareWorkspaceClient = () => new PrepareRepositoryWorkspaceEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const recheckPhysicalIdentityClient = () => new RecheckProjectPhysicalIdentityEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const projectGitEvidenceClient = () => new GetProjectGitEvidenceEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const captureGitWorkspaceCheckpointClient = () => new CaptureGitWorkspaceCheckpointEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const gitCheckpointChangedFilesClient = () => new GetGitCheckpointChangedFilesEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const gitCheckpointDiffClient = () => new GetGitCheckpointDiffEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const projectVerificationCommandsClient = () => new GetProjectVerificationCommandsEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const configureVerificationCommandClient = () => new ConfigureVerificationCommandEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const updateVerificationCommandClient = () => new UpdateVerificationCommandEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const deleteVerificationCommandClient = () => new DeleteVerificationCommandEndpointClient(getApiBaseUrl(), authenticatedHttp)

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
  PrepareRepositoryWorkspaceResponse,
  ProjectRunSummaryResponse,
  RecheckProjectPhysicalIdentityResponse,
  RegisterProjectResponse,
  RequestHostCapabilityRefreshResponse,
  RunEventResponse,
  StageMapEntryResponse,
  UpdateVerificationCommandRequest,
  VerificationCommandResponse,
} from './generated/api-client'
export { RegisterProjectRequest } from './generated/api-client'
