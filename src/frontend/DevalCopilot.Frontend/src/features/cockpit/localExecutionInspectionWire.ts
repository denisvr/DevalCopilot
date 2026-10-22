import {
  GetLocalExecutionSequenceInspectionEndpointClient,
  GetLocalExecutionStepInspectionEndpointClient,
  GetLocalExecutionVerificationOutputWindowEndpointClient,
} from '../../api/generated/api-client'
import type {
  GetLocalExecutionSequenceInspectionResponse,
  GetLocalExecutionStepInspectionResponse,
  GetLocalExecutionVerificationOutputWindowResponse,
} from '../../api/generated/api-client'

// The three recorded local execution inspection operations (ADR-0061), each answering a successful call with its parsed JSON body instead
// of the generated model. The generated models turn explicit nulls into undefined, drop unknown members and coerce types; the
// whole-answer decoders need the original types, exact keys and explicit nulls. An empty or non-JSON body resolves as `undefined`, which
// those decoders refuse. URL construction, authentication, the single request and every non-200 answer stay the generated operations'
// own; nothing is logged, stored, retried or aborted by these clients.

function parsedBody<T>(response: Response): Promise<T> {
  return response.text().then((body) => {
    try {
      return (body === '' ? undefined : JSON.parse(body)) as T
    } catch {
      return undefined as unknown as T
    }
  })
}

/** The recorded index read of one run's local execution sequence. */
export class LocalExecutionInspectionIndexWireClient extends GetLocalExecutionSequenceInspectionEndpointClient {
  /** The parsed 200 body for exactly this run, to be judged by the decoder; never a generated model. */
  readIndex(runId: string): Promise<unknown> {
    return this.getLocalExecutionSequenceInspection(runId)
  }

  protected override processGetLocalExecutionSequenceInspection(
    response: Response,
  ): Promise<GetLocalExecutionSequenceInspectionResponse> {
    return response.status === 200
      ? parsedBody<GetLocalExecutionSequenceInspectionResponse>(response)
      : super.processGetLocalExecutionSequenceInspection(response)
  }
}

/** The selected-operation read of one recorded step. */
export class LocalExecutionInspectionStepWireClient extends GetLocalExecutionStepInspectionEndpointClient {
  /** The parsed 200 body for exactly this run and step, to be judged by the decoder. */
  readStep(runId: string, stepId: string): Promise<unknown> {
    return this.getLocalExecutionStepInspection(runId, stepId)
  }

  protected override processGetLocalExecutionStepInspection(response: Response): Promise<GetLocalExecutionStepInspectionResponse> {
    return response.status === 200
      ? parsedBody<GetLocalExecutionStepInspectionResponse>(response)
      : super.processGetLocalExecutionStepInspection(response)
  }
}

/** One sealed verification stream window of a recorded step. */
export class LocalExecutionInspectionWindowWireClient extends GetLocalExecutionVerificationOutputWindowEndpointClient {
  /** The parsed 200 body of exactly this step, stream and byte window, to be judged by the decoder. */
  readWindow(runId: string, stepId: string, stream: 'stdout' | 'stderr', fromOffset: number, maxBytes: number): Promise<unknown> {
    return this.getLocalExecutionVerificationOutputWindow(runId, stepId, stream, fromOffset, maxBytes)
  }

  protected override processGetLocalExecutionVerificationOutputWindow(
    response: Response,
  ): Promise<GetLocalExecutionVerificationOutputWindowResponse> {
    return response.status === 200
      ? parsedBody<GetLocalExecutionVerificationOutputWindowResponse>(response)
      : super.processGetLocalExecutionVerificationOutputWindow(response)
  }
}
