import {
  ArmLocalExecutionSequenceEndpointClient,
  ArmLocalExecutionSequenceRequest,
  GetLocalExecutionQueueAvailabilityEndpointClient,
  GetLocalExecutionSequenceAvailabilityEndpointClient,
  GetLocalExecutionSequenceEndpointClient,
  QueueLocalExecutionSequenceEndpointClient,
  QueueLocalExecutionSequenceRequest,
  StopLocalExecutionSequenceEndpointClient,
} from '../../api/generated/api-client'
import type {
  ArmLocalExecutionSequenceResponse,
  GetLocalExecutionSequenceAvailabilityResponse,
  GetLocalExecutionSequenceResponse,
  StopLocalExecutionSequenceResponse,
} from '../../api/generated/api-client'
import type { LocalExecutionArmTuple } from './localExecutionSequence'

// The four local-execution-sequence operations (ADR-0060), each answering a successful call with its parsed JSON body instead of the
// generated model. The generated models turn explicit nulls into undefined, drop unknown members and coerce types; the whole-answer
// decoders need the original types, exact keys and explicit nulls. An empty or non-JSON body resolves as `undefined`, which those
// decoders refuse. URL construction, authentication, the single request and every non-200 answer stay the generated operations' own;
// nothing is logged, stored or retried. Each client may carry the signal that cancels its actual request.

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

/** The recorded status read of one run's local execution sequence. */
export class LocalExecutionSequenceStatusWireClient extends GetLocalExecutionSequenceEndpointClient {
  constructor(baseUrl: string, http: FetchLike, signal?: AbortSignal) {
    super(baseUrl, withAbortSignal(http, signal))
  }

  /** The parsed 200 body for exactly this run, to be judged by the decoder; never a generated model. */
  readLocalExecutionSequence(runId: string): Promise<unknown> {
    return this.getLocalExecutionSequence(runId)
  }

  protected override processGetLocalExecutionSequence(response: Response): Promise<GetLocalExecutionSequenceResponse> {
    return response.status === 200
      ? parsedBody<GetLocalExecutionSequenceResponse>(response)
      : super.processGetLocalExecutionSequence(response)
  }
}

/** The recorded arming availability of one explicitly selected plan. */
export class LocalExecutionSequenceAvailabilityWireClient extends GetLocalExecutionSequenceAvailabilityEndpointClient {
  constructor(baseUrl: string, http: FetchLike, signal?: AbortSignal) {
    super(baseUrl, withAbortSignal(http, signal))
  }

  readAvailability(runId: string, planProposalMessageId: string): Promise<unknown> {
    return this.getLocalExecutionSequenceAvailability(runId, planProposalMessageId)
  }

  protected override processGetLocalExecutionSequenceAvailability(response: Response): Promise<GetLocalExecutionSequenceAvailabilityResponse> {
    return response.status === 200
      ? parsedBody<GetLocalExecutionSequenceAvailabilityResponse>(response)
      : super.processGetLocalExecutionSequenceAvailability(response)
  }
}

/** The explicit arming of one run's local execution sequence; the body is exactly the five members of the request. */
export class ArmLocalExecutionSequenceWireClient extends ArmLocalExecutionSequenceEndpointClient {
  constructor(baseUrl: string, http: FetchLike, signal?: AbortSignal) {
    super(baseUrl, withAbortSignal(http, signal))
  }

  armLocalExecutionSequenceRaw(runId: string, tuple: LocalExecutionArmTuple): Promise<unknown> {
    return this.armLocalExecutionSequence(
      runId,
      new ArmLocalExecutionSequenceRequest({
        planProposalMessageId: tuple.planProposalMessageId,
        workspaceId: tuple.workspaceId,
        checkpointId: tuple.checkpointId,
        fingerprintSha256: tuple.fingerprintSha256,
        confirmationSha256: tuple.confirmationSha256,
      }),
    )
  }

  protected override processArmLocalExecutionSequence(response: Response): Promise<ArmLocalExecutionSequenceResponse> {
    return response.status === 200
      ? parsedBody<ArmLocalExecutionSequenceResponse>(response)
      : super.processArmLocalExecutionSequence(response)
  }
}

/** The bodyless Stop after current operation of exactly one run and sequence. */
export class StopLocalExecutionSequenceWireClient extends StopLocalExecutionSequenceEndpointClient {
  constructor(baseUrl: string, http: FetchLike, signal?: AbortSignal) {
    super(baseUrl, withAbortSignal(http, signal))
  }

  stopLocalExecutionSequenceRaw(runId: string, sequenceId: string): Promise<unknown> {
    return this.stopLocalExecutionSequence(runId, sequenceId)
  }

  protected override processStopLocalExecutionSequence(response: Response): Promise<StopLocalExecutionSequenceResponse> {
    return response.status === 200
      ? parsedBody<StopLocalExecutionSequenceResponse>(response)
      : super.processStopLocalExecutionSequence(response)
  }
}

/** ADR-0062: the recorded queue availability of one explicitly selected plan; the plan is the single required query key. */
export class LocalExecutionQueueAvailabilityWireClient extends GetLocalExecutionQueueAvailabilityEndpointClient {
  constructor(baseUrl: string, http: FetchLike, signal?: AbortSignal) {
    super(baseUrl, withAbortSignal(http, signal))
  }

  readQueueAvailability(runId: string, planProposalMessageId: string): Promise<unknown> {
    return this.getLocalExecutionQueueAvailability(runId, planProposalMessageId)
  }

  protected override processGetLocalExecutionQueueAvailability(response: Response): Promise<GetLocalExecutionSequenceAvailabilityResponse> {
    return response.status === 200
      ? parsedBody<GetLocalExecutionSequenceAvailabilityResponse>(response)
      : super.processGetLocalExecutionQueueAvailability(response)
  }
}

/** ADR-0062: the explicit queueing of one run's local execution sequence; the body is exactly the five members of the request. */
export class QueueLocalExecutionSequenceWireClient extends QueueLocalExecutionSequenceEndpointClient {
  constructor(baseUrl: string, http: FetchLike, signal?: AbortSignal) {
    super(baseUrl, withAbortSignal(http, signal))
  }

  queueLocalExecutionSequenceRaw(runId: string, tuple: LocalExecutionArmTuple): Promise<unknown> {
    return this.queueLocalExecutionSequence(
      runId,
      new QueueLocalExecutionSequenceRequest({
        planProposalMessageId: tuple.planProposalMessageId,
        workspaceId: tuple.workspaceId,
        checkpointId: tuple.checkpointId,
        fingerprintSha256: tuple.fingerprintSha256,
        confirmationSha256: tuple.confirmationSha256,
      }),
    )
  }

  protected override processQueueLocalExecutionSequence(response: Response): Promise<ArmLocalExecutionSequenceResponse> {
    return response.status === 200
      ? parsedBody<ArmLocalExecutionSequenceResponse>(response)
      : super.processQueueLocalExecutionSequence(response)
  }
}
