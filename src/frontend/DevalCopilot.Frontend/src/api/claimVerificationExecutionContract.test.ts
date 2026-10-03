import { describe, expect, it } from 'vitest'
import { ApiException, ClaimVerificationExecutionEndpointClient, ClaimVerificationExecutionRequest } from './generated/api-client'

// The host accepts a verification claim with HTTP 202 and a small JSON body. The generated client is the only way the cockpit
// reads that answer, so it must treat exactly that answer as success, and an error answer as a failure.
function clientAnswering(response: Response) {
  const fetch = () => Promise.resolve(response)
  return new ClaimVerificationExecutionEndpointClient('http://host.invalid', { fetch })
}

const request = new ClaimVerificationExecutionRequest({ gitCheckpointId: 'checkpoint-1' })

describe('ClaimVerificationExecutionEndpointClient', () => {
  it('returns the response DTO of the host\'s HTTP 202 acceptance', async () => {
    const body = JSON.stringify({ verificationExecutionId: 'execution-1', executionNumber: 3 })
    const client = clientAnswering(new Response(body, { status: 202, headers: { 'Content-Type': 'application/json' } }))

    const accepted = await client.claimVerificationExecution('project-1', 'command-1', request)

    expect(accepted.verificationExecutionId).toBe('execution-1')
    expect(accepted.executionNumber).toBe(3)
  })

  it('still rejects a refusal as an API exception instead of reporting an acceptance', async () => {
    const client = clientAnswering(new Response('{"errors":[]}', { status: 409, headers: { 'Content-Type': 'application/json' } }))

    await expect(client.claimVerificationExecution('project-1', 'command-1', request)).rejects.toSatisfy(
      (caught: unknown) => ApiException.isApiException(caught) && (caught as ApiException).status === 409,
    )
  })
})
