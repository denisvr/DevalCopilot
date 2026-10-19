import { GetAgentAttemptInputsEndpointClient } from '../../api/generated/api-client'
import type { GetAgentAttemptInputsResponse } from '../../api/generated/api-client'

/**
 * The generated recorded-inputs operation, answering a successful read with its parsed JSON body instead of the generated model. The
 * generated model turns both a required null `attempt` and malformed falsy values into undefined. The decoder needs their original
 * types and explicit nulls to judge each status shape; it copies only admitted fields. An empty or non-JSON body
 * resolves as `undefined`, which that decoder refuses. URL construction, authentication, the single request and every non-200 answer
 * stay the generated operation's own; nothing is logged or stored.
 */
export class AgentAttemptInputsWireClient extends GetAgentAttemptInputsEndpointClient {
  /**
   * The parsed 200 body of exactly this run and attempt, to be judged by the decoder; never a generated model. `limit` (ADR-0053) is
   * sent only when given; without it the host keeps its ten-reference default.
   */
  readAgentAttemptInputs(runId: string, attemptId: string, limit?: number): Promise<unknown> {
    return this.getAgentAttemptInputs(runId, attemptId, limit)
  }

  protected override processGetAgentAttemptInputs(response: Response): Promise<GetAgentAttemptInputsResponse> {
    if (response.status !== 200) {
      return super.processGetAgentAttemptInputs(response)
    }

    return response.text().then((body) => {
      try {
        return (body === '' ? undefined : JSON.parse(body)) as GetAgentAttemptInputsResponse
      } catch {
        return undefined as unknown as GetAgentAttemptInputsResponse
      }
    })
  }
}
