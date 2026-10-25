import {
  GetLocalExecutionOwnershipReleaseEndpointClient,
  ReleaseLocalExecutionOwnershipEndpointClient,
  ReleaseLocalExecutionOwnershipRequest,
} from '../../api/generated/api-client'
import type { LocalExecutionOwnershipReleaseResponse } from '../../api/generated/api-client'
import type { LocalExecutionReleaseCandidate } from './localExecutionOwnershipRelease'

// The two ownership release operations (ADR-0065), each answering a successful call with its parsed JSON body instead of the generated
// model. The generated models turn explicit nulls into undefined, drop unknown members and coerce types; the whole-answer decoder needs
// the original types, exact keys and explicit nulls. An empty or non-JSON body resolves as `undefined`, which the decoder refuses. URL
// construction, authentication, the single request and every non-200 answer stay the generated operations' own; nothing is logged,
// stored or retried. Each client carries the signal that cancels its actual request.

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

function withAbortSignal(http: FetchLike, signal: AbortSignal | undefined): FetchLike {
  return signal === undefined ? http : { fetch: (url, init) => http.fetch(url, { ...init, signal }) }
}

/** The bodyless recorded read of one step's ownership release candidate or receipt. */
export class LocalExecutionOwnershipReleaseReadWireClient extends GetLocalExecutionOwnershipReleaseEndpointClient {
  constructor(baseUrl: string, http: FetchLike, signal?: AbortSignal) {
    super(baseUrl, withAbortSignal(http, signal))
  }

  /** The parsed 200 body for exactly this run and step, to be judged by the decoder; never a generated model. */
  readRelease(runId: string, stepId: string): Promise<unknown> {
    return this.getLocalExecutionOwnershipRelease(runId, stepId)
  }

  protected override processGetLocalExecutionOwnershipRelease(response: Response): Promise<LocalExecutionOwnershipReleaseResponse> {
    return response.status === 200
      ? parsedBody<LocalExecutionOwnershipReleaseResponse>(response)
      : super.processGetLocalExecutionOwnershipRelease(response)
  }
}

/** The one explicit release of one step; the body is exactly the four members of the confirmation the Human saw. */
export class LocalExecutionOwnershipReleaseCommandWireClient extends ReleaseLocalExecutionOwnershipEndpointClient {
  constructor(baseUrl: string, http: FetchLike, signal?: AbortSignal) {
    super(baseUrl, withAbortSignal(http, signal))
  }

  releaseRaw(runId: string, stepId: string, candidate: LocalExecutionReleaseCandidate): Promise<unknown> {
    return this.releaseLocalExecutionOwnership(
      runId,
      stepId,
      new ReleaseLocalExecutionOwnershipRequest({
        observationId: candidate.observationId,
        confirmationVersion: candidate.confirmationVersion,
        confirmationSha256: candidate.confirmationSha256,
        confirmationExpiresAtUtc: candidate.confirmationExpiresAtUtc,
      }),
    )
  }

  protected override processReleaseLocalExecutionOwnership(response: Response): Promise<LocalExecutionOwnershipReleaseResponse> {
    return response.status === 200
      ? parsedBody<LocalExecutionOwnershipReleaseResponse>(response)
      : super.processReleaseLocalExecutionOwnership(response)
  }
}
