import { GetExecutionCapacityEndpointClient, SetExecutionCapacityLimitEndpointClient, SetExecutionCapacityLimitRequest } from '../../api/generated/api-client'
import type { GetExecutionCapacityResponse, SetExecutionCapacityLimitResponse } from '../../api/generated/api-client'

// The execution capacity read (ADR-0058) and the explicit limit change (ADR-0059), each answering a successful call with its parsed JSON
// body instead of the generated model. The generated model turns explicit nulls into undefined, drops unknown members and coerces types;
// the whole-answer decoder needs the original types, exact keys and explicit nulls to judge the whole answer (a missing key is not an
// explicit null). An empty or non-JSON body resolves as `undefined`, which that decoder refuses. URL construction, authentication, the
// single request and every non-200 answer stay the generated operation's own; nothing is logged, stored or retried.

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

/** The current host read of how many potentially mutating execution owners the host records. */
export class ExecutionCapacityWireClient extends GetExecutionCapacityEndpointClient {
  /** `signal`, when given, cancels this client's actual request. */
  constructor(baseUrl: string, http: FetchLike, signal?: AbortSignal) {
    super(baseUrl, withAbortSignal(http, signal))
  }

  /** The parsed 200 body, to be judged by the decoder; never a generated model. */
  readExecutionCapacity(): Promise<unknown> {
    return this.getExecutionCapacity()
  }

  protected override processGetExecutionCapacity(response: Response): Promise<GetExecutionCapacityResponse> {
    return response.status === 200 ? parsedBody<GetExecutionCapacityResponse>(response) : super.processGetExecutionCapacity(response)
  }
}

/** The one explicit change of the recorded-execution limit. It never retries: a caller that wants another attempt makes it explicitly. */
export class ExecutionCapacityLimitWireClient extends SetExecutionCapacityLimitEndpointClient {
  /** `signal`, when given, cancels this client's actual request. */
  constructor(baseUrl: string, http: FetchLike, signal?: AbortSignal) {
    super(baseUrl, withAbortSignal(http, signal))
  }

  /** The parsed 200 body, to be judged by the decoder; never a generated model. Declared refusals reject as the shared problem. */
  changeExecutionCapacityLimit(limit: number): Promise<unknown> {
    return this.setExecutionCapacityLimit(new SetExecutionCapacityLimitRequest({ limit }))
  }

  protected override processSetExecutionCapacityLimit(response: Response): Promise<SetExecutionCapacityLimitResponse> {
    return response.status === 200 ? parsedBody<SetExecutionCapacityLimitResponse>(response) : super.processSetExecutionCapacityLimit(response)
  }
}
