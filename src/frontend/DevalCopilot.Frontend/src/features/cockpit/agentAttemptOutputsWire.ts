import { GetAgentAttemptOutputsEndpointClient } from '../../api/generated/api-client'
import type { GetAgentAttemptOutputsResponse } from '../../api/generated/api-client'

/**
 * The generated recorded-outputs operation (ADR-0054), answering a successful read with its parsed JSON body instead of the generated
 * model. The generated model turns both a required null `attempt` or count and malformed falsy values into undefined and would drop
 * unknown members; the decoder needs their original types, exact keys and explicit nulls to judge each status shape. An empty or
 * non-JSON body resolves as `undefined`, which that decoder refuses. URL construction, authentication, the single request and every
 * non-200 answer stay the generated operation's own; nothing is logged or stored.
 */
export class AgentAttemptOutputsWireClient extends GetAgentAttemptOutputsEndpointClient {
  /** The parsed 200 body of exactly this run and attempt, to be judged by the decoder; never a generated model. */
  readAgentAttemptOutputs(runId: string, attemptId: string): Promise<unknown> {
    return this.getAgentAttemptOutputs(runId, attemptId)
  }

  protected override processGetAgentAttemptOutputs(response: Response): Promise<GetAgentAttemptOutputsResponse> {
    if (response.status !== 200) {
      return super.processGetAgentAttemptOutputs(response)
    }

    return response.text().then((body) => {
      try {
        return (body === '' ? undefined : JSON.parse(body)) as GetAgentAttemptOutputsResponse
      } catch {
        return undefined as unknown as GetAgentAttemptOutputsResponse
      }
    })
  }
}
