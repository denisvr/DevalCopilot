import {
  GetProcessAttemptOutputEndpointClient,
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

export type {
  CapabilityReadinessResponse,
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
} from './generated/api-client'
export { RegisterProjectRequest } from './generated/api-client'
