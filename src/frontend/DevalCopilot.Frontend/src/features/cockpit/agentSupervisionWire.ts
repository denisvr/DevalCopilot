import { GetAgentSupervisionEndpointClient } from '../../api/generated/api-client'
import type { GetAgentSupervisionResponse } from '../../api/generated/api-client'

// The Agent supervision read (ADR-0056), answering a successful call with its parsed JSON body instead of the generated model. The
// generated model turns explicit nulls and malformed falsy values into undefined, drops unknown members and coerces types; the whole-answer
// decoder needs the original types, exact keys and explicit nulls to judge the whole answer. An empty or non-JSON body resolves as
// `undefined`, which that decoder refuses. URL construction, authentication, the single request and every non-200 answer stay the
// generated operation's own; nothing is logged, stored or retried.

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
 * The transport of this one operation, carrying the read's own cancellation (ASUP-R2): the generated operation exposes no AbortSignal, so
 * the signal is added to the request it builds, and aborting it cancels the actual request rather than only abandoning its answer. The
 * shared authenticated transport and the generator configuration are unchanged.
 */
function withAbortSignal(http: FetchLike, signal: AbortSignal | undefined): FetchLike {
  return signal === undefined ? http : { fetch: (url, init) => http.fetch(url, { ...init, signal }) }
}

/** The current host supervision read of one run's selected Agent attempt. */
export class AgentSupervisionWireClient extends GetAgentSupervisionEndpointClient {
  /** `signal`, when given, cancels this client's actual request. */
  constructor(baseUrl: string, http: FetchLike, signal?: AbortSignal) {
    super(baseUrl, withAbortSignal(http, signal))
  }

  /** The parsed 200 body for exactly this run, to be judged by the decoder; never a generated model. */
  readAgentSupervision(runId: string): Promise<unknown> {
    return this.getAgentSupervision(runId)
  }

  protected override processGetAgentSupervision(response: Response): Promise<GetAgentSupervisionResponse> {
    return response.status === 200 ? parsedBody<GetAgentSupervisionResponse>(response) : super.processGetAgentSupervision(response)
  }
}
