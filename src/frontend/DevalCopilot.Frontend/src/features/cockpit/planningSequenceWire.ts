import {
  ArmPlanningSequenceEndpointClient,
  GetPlanningSequenceEndpointClient,
  StopPlanningSequenceEndpointClient,
} from '../../api/generated/api-client'
import type {
  ArmPlanningSequenceRequest,
  ArmPlanningSequenceResponse,
  GetPlanningSequenceResponse,
  StopPlanningSequenceResponse,
} from '../../api/generated/api-client'

// The three planning-sequence operations (ADR-0055), each answering a successful call with its parsed JSON body instead of the generated
// model. The generated models turn explicit nulls and malformed falsy values into undefined, drop unknown members and coerce types; the
// decoders need the original types, exact keys and explicit nulls to judge the whole answer. An empty or non-JSON body resolves as
// `undefined`, which those decoders refuse. URL construction, authentication, the single request and every non-200 answer stay the
// generated operations' own; nothing is logged, stored or retried.

function parsedBody<T>(response: Response): Promise<T> {
  return response.text().then((body) => {
    try {
      return (body === '' ? undefined : JSON.parse(body)) as T
    } catch {
      return undefined as unknown as T
    }
  })
}

/** The recorded status read of one run's planning sequence. */
export class PlanningSequenceStatusWireClient extends GetPlanningSequenceEndpointClient {
  /** The parsed 200 body for exactly this run, to be judged by the decoder; never a generated model. */
  readPlanningSequence(runId: string): Promise<unknown> {
    return this.getPlanningSequence(runId)
  }

  protected override processGetPlanningSequence(response: Response): Promise<GetPlanningSequenceResponse> {
    return response.status === 200 ? parsedBody<GetPlanningSequenceResponse>(response) : super.processGetPlanningSequence(response)
  }
}

/** The explicit arming of one run's planning sequence; the body is exactly the three members of the request. */
export class ArmPlanningSequenceWireClient extends ArmPlanningSequenceEndpointClient {
  /** The parsed 200 receipt of arming exactly this run, to be judged by the decoder against the tuple that was sent. */
  armPlanningSequenceRaw(runId: string, request: ArmPlanningSequenceRequest): Promise<unknown> {
    return this.armPlanningSequence(runId, request)
  }

  protected override processArmPlanningSequence(response: Response): Promise<ArmPlanningSequenceResponse> {
    return response.status === 200 ? parsedBody<ArmPlanningSequenceResponse>(response) : super.processArmPlanningSequence(response)
  }
}

/** The bodyless Stop after current step of exactly one run and sequence. */
export class StopPlanningSequenceWireClient extends StopPlanningSequenceEndpointClient {
  /** The parsed 200 answer of stopping exactly this run and sequence, to be judged by the decoder. */
  stopPlanningSequenceRaw(runId: string, sequenceId: string): Promise<unknown> {
    return this.stopPlanningSequence(runId, sequenceId)
  }

  protected override processStopPlanningSequence(response: Response): Promise<StopPlanningSequenceResponse> {
    return response.status === 200 ? parsedBody<StopPlanningSequenceResponse>(response) : super.processStopPlanningSequence(response)
  }
}
