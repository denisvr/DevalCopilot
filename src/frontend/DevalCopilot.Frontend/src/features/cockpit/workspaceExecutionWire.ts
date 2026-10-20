import { GetWorkspaceExecutionStatusEndpointClient } from '../../api/generated/api-client'
import type { GetWorkspaceExecutionStatusResponse } from '../../api/generated/api-client'

// The workspace execution status read (ADR-0057), answering a successful call with its parsed JSON body instead of the generated model. The
// generated model turns explicit nulls into undefined, drops unknown members and coerces types; the whole-answer decoder needs the original
// types, exact keys and explicit nulls to judge the whole answer (a missing key is not an explicit null). An empty or non-JSON body
// resolves as `undefined`, which that decoder refuses. URL construction, authentication, the single request and every non-200 answer stay
// the generated operation's own; nothing is logged, stored or retried.

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

/**
 * The transport of this one operation, carrying the read's own cancellation: the generated operation exposes no AbortSignal, so the
 * signal is added to the request it builds, and aborting it cancels the actual request rather than only abandoning its answer. The
 * shared authenticated transport and the generator configuration are unchanged.
 */
function withAbortSignal(http: FetchLike, signal: AbortSignal | undefined): FetchLike {
  return signal === undefined ? http : { fetch: (url, init) => http.fetch(url, { ...init, signal }) }
}

/** The current host read of who, if anyone, owns execution in one project's latest workspace. */
export class WorkspaceExecutionWireClient extends GetWorkspaceExecutionStatusEndpointClient {
  /** `signal`, when given, cancels this client's actual request. */
  constructor(baseUrl: string, http: FetchLike, signal?: AbortSignal) {
    super(baseUrl, withAbortSignal(http, signal))
  }

  /** The parsed 200 body for exactly this project, to be judged by the decoder; never a generated model. */
  readWorkspaceExecutionStatus(projectId: string): Promise<unknown> {
    return this.getWorkspaceExecutionStatus(projectId)
  }

  protected override processGetWorkspaceExecutionStatus(response: Response): Promise<GetWorkspaceExecutionStatusResponse> {
    return response.status === 200
      ? parsedBody<GetWorkspaceExecutionStatusResponse>(response)
      : super.processGetWorkspaceExecutionStatus(response)
  }
}
