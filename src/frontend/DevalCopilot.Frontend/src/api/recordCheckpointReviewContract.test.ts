import { describe, expect, it, vi } from 'vitest'
import { ApiException, RecordCheckpointReviewEndpointClient, RecordCheckpointReviewRequest } from './generated/api-client'

// The host records a manual checkpoint review with HTTP 201 (Created), a Location and a small JSON body. The generated client is
// the only way the cockpit reads that answer, so it must resolve exactly that answer with the typed review identity and
// decision, send the request once, and still reject a refusal with its Problem Details.
function clientAnswering(response: Response) {
  const fetch = vi.fn(() => Promise.resolve(response))
  return { client: new RecordCheckpointReviewEndpointClient('http://host.invalid', { fetch }), fetch }
}

const request = new RecordCheckpointReviewRequest({
  gitCheckpointId: 'checkpoint-1',
  verificationExecutionId: 'execution-1',
  actorKind: 'Human',
  decision: 'Approved',
})

describe('RecordCheckpointReviewEndpointClient', () => {
  it('resolves the HTTP 201 acceptance with the typed review identity and decision, sending the request once', async () => {
    const body = JSON.stringify({ reviewId: 'review-1', decision: 'Approved' })
    const { client, fetch } = clientAnswering(
      new Response(body, { status: 201, headers: { 'Content-Type': 'application/json', Location: '/api/projects/project-1/reviews' } }),
    )

    const recorded = await client.recordCheckpointReview('project-1', request)

    expect(recorded.reviewId).toBe('review-1')
    expect(recorded.decision).toBe('Approved')
    expect(fetch).toHaveBeenCalledTimes(1)
    const [url, init] = fetch.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toBe('http://host.invalid/api/projects/project-1/reviews')
    expect(init.method).toBe('POST')
    expect(JSON.parse(init.body as string)).toEqual({
      gitCheckpointId: 'checkpoint-1',
      verificationExecutionId: 'execution-1',
      actorKind: 'Human',
      decision: 'Approved',
    })
  })

  it('rejects a conflict as an API exception that keeps its Problem Details, never as an acceptance', async () => {
    const problem = JSON.stringify({ errors: [{ code: 'reviews.checkpoint_not_current', detail: 'The selected source checkpoint is no longer current.' }] })
    const { client, fetch } = clientAnswering(new Response(problem, { status: 409, headers: { 'Content-Type': 'application/json' } }))

    const refusal = await client.recordCheckpointReview('project-1', request).then(
      () => null,
      (caught: unknown) => caught,
    )

    expect(ApiException.isApiException(refusal)).toBe(true)
    expect((refusal as ApiException).status).toBe(409)
    expect((refusal as ApiException).response).toContain('reviews.checkpoint_not_current')
    expect(fetch).toHaveBeenCalledTimes(1)
  })
})
