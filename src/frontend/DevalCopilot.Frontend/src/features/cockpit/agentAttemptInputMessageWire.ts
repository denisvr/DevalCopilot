import { GetAgentAttemptInputMessageEndpointClient } from '../../api/generated/api-client'
import type { GetAgentAttemptInputMessageResponse } from '../../api/generated/api-client'

/**
 * The generated selected-input content operation (ADR-0053), answering a successful read with its parsed JSON body instead of the
 * generated model. The generated model turns required nulls and malformed falsy values into undefined and would drop unknown members;
 * the decoder needs the exact keys, their original types and explicit nulls to judge the closed shape. An empty or non-JSON body
 * resolves as `undefined`, which that decoder refuses. URL construction, authentication, the single request and every non-200 answer
 * stay the generated operation's own; nothing is logged or stored.
 */
export class AgentAttemptInputMessageWireClient extends GetAgentAttemptInputMessageEndpointClient {
  /** The parsed 200 body of exactly this run, attempt and input message, to be judged by the decoder; never a generated model. */
  readAgentAttemptInputMessage(runId: string, attemptId: string, messageId: string): Promise<unknown> {
    return this.getAgentAttemptInputMessage(runId, attemptId, messageId)
  }

  protected override processGetAgentAttemptInputMessage(response: Response): Promise<GetAgentAttemptInputMessageResponse> {
    if (response.status !== 200) {
      return super.processGetAgentAttemptInputMessage(response)
    }

    return response.text().then((body) => {
      try {
        return (body === '' ? undefined : JSON.parse(body)) as GetAgentAttemptInputMessageResponse
      } catch {
        return undefined as unknown as GetAgentAttemptInputMessageResponse
      }
    })
  }
}
