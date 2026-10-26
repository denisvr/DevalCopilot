import {
  GetPlanningSequenceInspectionEndpointClient,
  GetPlanningStepInspectionEndpointClient,
} from '../../api/generated/api-client'
import type { GetPlanningSequenceInspectionResponse, GetPlanningStepInspectionResponse } from '../../api/generated/api-client'

// The recorded planning sequence inspection operations (ADR-0068), each answering a successful call with its parsed JSON body instead of
// the generated model. The generated models turn explicit nulls into undefined, drop unknown members and coerce types; the whole-answer
// decoders need the original types, exact keys and explicit nulls. An empty or non-JSON body resolves as `undefined`, which those
// decoders refuse. URL construction, authentication, the single request and every non-200 answer stay the generated operations' own;
// nothing is logged, stored, retried or aborted by these clients.

function parsedBody<T>(response: Response): Promise<T> {
  return response.text().then((body) => {
    try {
      return (body === '' ? undefined : JSON.parse(body)) as T
    } catch {
      return undefined as unknown as T
    }
  })
}

/** The recorded index read of one run's planning sequence. */
export class PlanningSequenceInspectionIndexWireClient extends GetPlanningSequenceInspectionEndpointClient {
  /** The parsed 200 body for exactly this run, to be judged by the decoder; never a generated model. */
  readIndex(runId: string): Promise<unknown> {
    return this.getPlanningSequenceInspection(runId)
  }

  protected override processGetPlanningSequenceInspection(response: Response): Promise<GetPlanningSequenceInspectionResponse> {
    return response.status === 200
      ? parsedBody<GetPlanningSequenceInspectionResponse>(response)
      : super.processGetPlanningSequenceInspection(response)
  }
}

/** The selected-association read of one recorded step. */
export class PlanningSequenceInspectionStepWireClient extends GetPlanningStepInspectionEndpointClient {
  /** The parsed 200 body for exactly this run and step, to be judged by the decoder. */
  readStep(runId: string, stepId: string): Promise<unknown> {
    return this.getPlanningStepInspection(runId, stepId)
  }

  protected override processGetPlanningStepInspection(response: Response): Promise<GetPlanningStepInspectionResponse> {
    return response.status === 200
      ? parsedBody<GetPlanningStepInspectionResponse>(response)
      : super.processGetPlanningStepInspection(response)
  }
}
