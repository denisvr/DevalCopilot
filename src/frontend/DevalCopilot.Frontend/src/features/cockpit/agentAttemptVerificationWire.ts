import {
  GetAgentAttemptVerificationEvidenceEndpointClient,
  GetAgentAttemptVerificationOutputWindowEndpointClient,
} from '../../api/generated/api-client'
import type {
  GetAgentAttemptVerificationEvidenceResponse,
  GetAgentAttemptVerificationOutputWindowResponse,
} from '../../api/generated/api-client'

// A successful answer is the parsed JSON body, never the generated model: the model turns explicit nulls and malformed falsy values
// into undefined, while the whole-answer decoders need the original types and nulls to judge each status shape. An empty or non-JSON
// body resolves as `undefined`, which those decoders refuse.
function parseBody(response: Response): Promise<unknown> {
  return response.text().then((body) => {
    try {
      return body === '' ? undefined : JSON.parse(body)
    } catch {
      return undefined
    }
  })
}

/**
 * The generated recorded-verification metadata operation (ADR-0052), answering a successful read with its parsed JSON body. URL
 * construction, authentication, the single request and every non-200 answer stay the generated operation's own; nothing is logged or
 * stored.
 */
export class AgentAttemptVerificationEvidenceWireClient extends GetAgentAttemptVerificationEvidenceEndpointClient {
  /** The parsed 200 body of exactly this run and attempt, to be judged by the decoder. */
  readVerificationEvidence(runId: string, attemptId: string): Promise<unknown> {
    return this.getAgentAttemptVerificationEvidence(runId, attemptId)
  }

  protected override processGetAgentAttemptVerificationEvidence(
    response: Response,
  ): Promise<GetAgentAttemptVerificationEvidenceResponse> {
    return response.status !== 200
      ? super.processGetAgentAttemptVerificationEvidence(response)
      : (parseBody(response) as Promise<GetAgentAttemptVerificationEvidenceResponse>)
  }
}

/** The generated verification output window operation (ADR-0052), answering a successful read with its parsed JSON body. */
export class AgentAttemptVerificationWindowWireClient extends GetAgentAttemptVerificationOutputWindowEndpointClient {
  /** The parsed 200 body of exactly this member, stream and byte window, to be judged by the decoder. */
  readVerificationWindow(
    runId: string,
    attemptId: string,
    executionId: string,
    stream: 'stdout' | 'stderr',
    fromOffset: number,
    maxBytes: number,
  ): Promise<unknown> {
    return this.getAgentAttemptVerificationOutputWindow(runId, attemptId, executionId, stream, fromOffset, maxBytes)
  }

  protected override processGetAgentAttemptVerificationOutputWindow(
    response: Response,
  ): Promise<GetAgentAttemptVerificationOutputWindowResponse> {
    return response.status !== 200
      ? super.processGetAgentAttemptVerificationOutputWindow(response)
      : (parseBody(response) as Promise<GetAgentAttemptVerificationOutputWindowResponse>)
  }
}
