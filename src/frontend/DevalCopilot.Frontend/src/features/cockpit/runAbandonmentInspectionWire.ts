import { GetRunAbandonmentInspectionEndpointClient } from '../../api/generated/api-client'
import type { GetRunAbandonmentInspectionResponse } from '../../api/generated/api-client'

// The recorded abandonment read of one run (ADR-0071), answering a successful call with its parsed JSON body instead of the generated
// model. The generated model turns explicit nulls into undefined, drops unknown members and coerces types; the whole-answer decoder
// needs the original types, exact keys and explicit nulls to judge the whole answer (a missing key is not an explicit null). An empty
// or non-JSON body resolves as `undefined`, which that decoder refuses. URL construction, authentication, the single request and every
// non-200 answer stay the generated operation's own; nothing is logged, stored or retried. The one GET carries its own AbortSignal into
// the actual request, so ending its visit or its ten-second bound cancels the transport itself.

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

/** The bodyless recorded read of one run's abandonment. */
export class RunAbandonmentInspectionWireClient extends GetRunAbandonmentInspectionEndpointClient {
  /** `signal`, when given, cancels this client's actual request. */
  constructor(baseUrl: string, http: FetchLike, signal?: AbortSignal) {
    super(baseUrl, withAbortSignal(http, signal))
  }

  /** The parsed 200 body for exactly this run, to be judged by the decoder; never a generated model. */
  readAbandonment(runId: string): Promise<unknown> {
    return this.getRunAbandonmentInspection(runId)
  }

  protected override processGetRunAbandonmentInspection(response: Response): Promise<GetRunAbandonmentInspectionResponse> {
    return response.status === 200
      ? parsedBody<GetRunAbandonmentInspectionResponse>(response)
      : super.processGetRunAbandonmentInspection(response)
  }
}
