import {
  GetProjectRunSummariesEndpointClient,
  GetRunCockpitEndpointClient,
  GetRunEventsEndpointClient,
  RequestHostCapabilityRefreshEndpointClient,
  StartSimulatedRunEndpointClient,
} from './generated/api-client'
import { authenticatedHttp, getApiBaseUrl } from './httpClient'

// One instance per endpoint client is enough for this slice; components import the
// factory functions rather than constructing a client or a URL themselves.
export const projectsClient = () => new GetProjectRunSummariesEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const startSimulatedRunClient = () => new StartSimulatedRunEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const runCockpitClient = () => new GetRunCockpitEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const runEventsClient = () => new GetRunEventsEndpointClient(getApiBaseUrl(), authenticatedHttp)
export const environmentClient = () => new RequestHostCapabilityRefreshEndpointClient(getApiBaseUrl(), authenticatedHttp)

export type {
  CapabilityReadinessResponse,
  GetRunCockpitResponse,
  ProjectRunSummaryResponse,
  RequestHostCapabilityRefreshResponse,
  RunEventResponse,
  StageMapEntryResponse,
} from './generated/api-client'
