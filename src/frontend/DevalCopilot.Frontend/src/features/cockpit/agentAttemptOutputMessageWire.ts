import { GetAgentAttemptOutputMessageEndpointClient } from '../../api/generated/api-client'
import type { GetAgentAttemptOutputMessageResponse } from '../../api/generated/api-client'

/**
 * The generated selected-output content operation (ADR-0054), answering a successful read with its parsed JSON body instead of the
 * generated model. The generated model turns required nulls and malformed falsy values into undefined and would drop unknown members;
 * the decoder needs the exact keys, their original types and explicit nulls to judge the closed shape. An empty or non-JSON body
 * resolves as `undefined`, which that decoder refuses. URL construction, authentication, the single request and every non-200 answer
 * stay the generated operation's own; nothing is logged or stored.
 */
export class AgentAttemptOutputMessageWireClient extends GetAgentAttemptOutputMessageEndpointClient {
  /** The parsed 200 body of exactly this run, attempt and output message, to be judged by the decoder; never a generated model. */
  readAgentAttemptOutputMessage(runId: string, attemptId: string, messageId: string): Promise<unknown> {
    return this.getAgentAttemptOutputMessage(runId, attemptId, messageId)
  }

  protected override processGetAgentAttemptOutputMessage(response: Response): Promise<GetAgentAttemptOutputMessageResponse> {
    if (response.status !== 200) {
      return super.processGetAgentAttemptOutputMessage(response)
    }

    return response.text().then((body) => {
      try {
        return (body === '' ? undefined : JSON.parse(body)) as GetAgentAttemptOutputMessageResponse
      } catch {
        return undefined as unknown as GetAgentAttemptOutputMessageResponse
      }
    })
  }
}
