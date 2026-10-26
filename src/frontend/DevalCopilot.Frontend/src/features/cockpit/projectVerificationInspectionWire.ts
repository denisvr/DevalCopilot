import {
  GetProjectVerificationHistoryEndpointClient,
  GetProjectVerificationInspectionEndpointClient,
  GetProjectVerificationOutputWindowEndpointClient,
} from '../../api/generated/api-client'
import type {
  GetProjectVerificationHistoryResponse,
  GetProjectVerificationInspectionResponse,
  GetProjectVerificationOutputWindowResponse,
} from '../../api/generated/api-client'

// The three recorded project verification operations (ADR-0069), each answering a successful call with its parsed JSON body instead of
// the generated model. The generated models turn explicit nulls into undefined, drop unknown members and coerce types; the whole-answer
// decoders need the original types, exact keys and explicit nulls (a missing key is not an explicit null). An empty or non-JSON body
// resolves as `undefined`, which those decoders refuse. URL construction, authentication, the single request and every non-200 answer
// stay the generated operations' own; nothing is logged, stored or retried. Each client carries its own AbortSignal into the actual
// request, so ending its visit or its ten-second bound cancels the transport itself.

function parsedBody<T>(response: Response): Promise<T> {
  return response.text().then((body) => {
    try {
      return (body === '' ? undefined : JSON.parse(body)) as T
    } catch {
      return undefined as unknown as T
    }
  })
}

type FetchLike = { fetch(url: RequestInfo, init?: RequestInit): Promise<Response> }

/** The transport of one operation, carrying its own cancellation: aborting the signal cancels the actual request. */
function withAbortSignal(http: FetchLike, signal: AbortSignal | undefined): FetchLike {
  return signal === undefined ? http : { fetch: (url, init) => http.fetch(url, { ...init, signal }) }
}

/** The bodyless keyset page of a project's recorded verification executions, newest first. */
export class ProjectVerificationHistoryWireClient extends GetProjectVerificationHistoryEndpointClient {
  constructor(baseUrl: string, http: FetchLike, signal?: AbortSignal) {
    super(baseUrl, withAbortSignal(http, signal))
  }

  /** The parsed 200 body for exactly this project and cursor (null: the newest page), to be judged by the decoder. */
  readPage(projectId: string, beforeExecutionNumber: number | null): Promise<unknown> {
    return this.getProjectVerificationHistory(projectId, beforeExecutionNumber)
  }

  protected override processGetProjectVerificationHistory(response: Response): Promise<GetProjectVerificationHistoryResponse> {
    return response.status === 200
      ? parsedBody<GetProjectVerificationHistoryResponse>(response)
      : super.processGetProjectVerificationHistory(response)
  }
}

/** The bodyless metadata read of one recorded execution. */
export class ProjectVerificationInspectionWireClient extends GetProjectVerificationInspectionEndpointClient {
  constructor(baseUrl: string, http: FetchLike, signal?: AbortSignal) {
    super(baseUrl, withAbortSignal(http, signal))
  }

  /** The parsed 200 body for exactly this project and execution, to be judged by the decoder. */
  readExecution(projectId: string, executionId: string): Promise<unknown> {
    return this.getProjectVerificationInspection(projectId, executionId)
  }

  protected override processGetProjectVerificationInspection(
    response: Response,
  ): Promise<GetProjectVerificationInspectionResponse> {
    return response.status === 200
      ? parsedBody<GetProjectVerificationInspectionResponse>(response)
      : super.processGetProjectVerificationInspection(response)
  }
}

/** The bodyless sealed output window read of one stream of one recorded execution. */
export class ProjectVerificationWindowWireClient extends GetProjectVerificationOutputWindowEndpointClient {
  constructor(baseUrl: string, http: FetchLike, signal?: AbortSignal) {
    super(baseUrl, withAbortSignal(http, signal))
  }

  /** The parsed 200 body for exactly this execution, stream and byte window, to be judged by the decoder. */
  readWindow(
    projectId: string,
    executionId: string,
    stream: 'stdout' | 'stderr',
    fromOffset: number,
    maxBytes: number,
  ): Promise<unknown> {
    return this.getProjectVerificationOutputWindow(projectId, executionId, stream, fromOffset, maxBytes)
  }

  protected override processGetProjectVerificationOutputWindow(
    response: Response,
  ): Promise<GetProjectVerificationOutputWindowResponse> {
    return response.status === 200
      ? parsedBody<GetProjectVerificationOutputWindowResponse>(response)
      : super.processGetProjectVerificationOutputWindow(response)
  }
}
