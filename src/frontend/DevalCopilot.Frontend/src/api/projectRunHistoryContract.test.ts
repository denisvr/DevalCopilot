import { describe, expect, it, vi } from 'vitest'
import { ApiError, ApiException, ApiProblemDetails, GetProjectRunHistoryEndpointClient } from './generated/api-client'

// The protected history read goes through the generated client only. It resolves the typed page (entries with revived UTC times and
// an optional located source), sends the exclusive cursor and the limit as query parameters, keeps the fixed code of a refusal,
// and never splices the project into the path.
function answering(response: Response) {
  const fetch = vi.fn(() => Promise.resolve(response))
  return { fetch, historyClient: new GetProjectRunHistoryEndpointClient('http://host.invalid', { fetch }) }
}

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

describe('GetProjectRunHistoryEndpointClient', () => {
  it('reads one page with a bodyless GET and resolves its typed entries, sources and continuation', async () => {
    const { historyClient, fetch } = answering(
      json({
        projectId: 'project-1',
        entries: [
          {
            projectId: 'project-1',
            runId: 'run-2',
            executionNumber: 2,
            objective: 'Second objective',
            lifecycle: 'Completed',
            stage: 'Completed',
            executionMode: 'ManualAgent',
            createdAtUtc: '2026-10-09T10:00:00+00:00',
            lastAdvancedAtUtc: '2026-10-09T11:00:00+00:00',
            receiptSource: { runId: 'run-2', operationId: 'operation-2', commitSha: 'c'.repeat(40), checkpointId: 'checkpoint-2', checkpointNumber: 3 },
          },
          {
            projectId: 'project-1',
            runId: 'run-1',
            executionNumber: 1,
            objective: 'First objective',
            lifecycle: 'Unrecognized',
            stage: 'Unrecognized',
            executionMode: 'Unrecognized',
            createdAtUtc: '2026-10-08T10:00:00+00:00',
            lastAdvancedAtUtc: '2026-10-08T11:00:00+00:00',
            receiptSource: null,
          },
        ],
        hasMore: true,
        nextBeforeExecutionNumber: 1,
      }),
    )

    const page = await historyClient.getProjectRunHistory('project-1', 3, 10)

    expect(fetch).toHaveBeenCalledTimes(1)
    const [url, init] = fetch.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toBe('http://host.invalid/api/projects/project-1/run-history?beforeExecutionNumber=3&limit=10')
    expect(init.method).toBe('GET')
    expect(init.body).toBeUndefined()
    expect(page.projectId).toBe('project-1')
    expect(page.entries?.map((entry) => entry.executionNumber)).toEqual([2, 1])
    expect(page.entries?.[0].receiptSource?.checkpointNumber).toBe(3)
    expect(page.entries?.[0].createdAtUtc).toBeInstanceOf(Date)
    expect(page.entries?.[0].lastAdvancedAtUtc).toBeInstanceOf(Date)
    expect(page.entries?.[1].receiptSource).toBeUndefined()
    expect(page.entries?.[1].lifecycle).toBe('Unrecognized')
    expect(page.hasMore).toBe(true)
    expect(page.nextBeforeExecutionNumber).toBe(1)
  })

  it('omits an absent cursor and limit and resolves the end of history without a next cursor', async () => {
    const { historyClient, fetch } = answering(json({ projectId: 'project-1', entries: [], hasMore: false, nextBeforeExecutionNumber: null }))

    const page = await historyClient.getProjectRunHistory('project-1', null, undefined)

    const [url] = fetch.mock.calls[0] as unknown as [string]
    expect(url).toBe('http://host.invalid/api/projects/project-1/run-history')
    expect(page.entries).toEqual([])
    expect(page.hasMore).toBe(false)
    expect(page.nextBeforeExecutionNumber ?? null).toBeNull()
  })

  it('rejects an undeclared status as an ApiException that keeps its response, never as a page', async () => {
    const { historyClient } = answering(json({ errors: [{ code: 'host.unavailable', detail: 'fixed' }] }, 503))

    const refusal = await historyClient.getProjectRunHistory('project-1', null, null).then(
      () => null,
      (caught: unknown) => caught,
    )

    expect(ApiException.isApiException(refusal)).toBe(true)
    expect((refusal as ApiException).status).toBe(503)
    expect((refusal as ApiException).response).toContain('host.unavailable')
  })

  it('rejects a 400 as the typed shared problem with its structured errors', async () => {
    const { historyClient } = answering(
      json(
        {
          type: 'urn:devalente:problem:validation',
          title: 'One or more validation errors occurred.',
          status: 400,
          detail: 'See the errors property for details.',
          instance: '/api/projects/project-1/run-history',
          traceId: '00-abc-def-00',
          errors: [
            { code: 'validation.invalid_limit', detail: 'The limit must be a whole number from 1 to 20.', pointer: '#/limit', parameters: null },
            { code: 'validation.invalid_cursor', detail: 'The cursor must be a positive whole number.', pointer: '#/beforeExecutionNumber', parameters: null },
          ],
        },
        400,
      ),
    )

    const refusal = await historyClient.getProjectRunHistory('project-1', 0, 0).then(
      () => null,
      (caught: unknown) => caught as ApiProblemDetails,
    )

    const problem = refusal as ApiProblemDetails
    expect(problem).toBeInstanceOf(ApiProblemDetails)
    expect(problem.status).toBe(400)
    expect(problem.type).toBe('urn:devalente:problem:validation')
    expect(problem.traceId).toBe('00-abc-def-00')
    expect(problem.errors).toHaveLength(2)
    expect(problem.errors?.[0]).toBeInstanceOf(ApiError)
    expect(problem.errors?.map((error) => [error.code, error.pointer])).toEqual([
      ['validation.invalid_limit', '#/limit'],
      ['validation.invalid_cursor', '#/beforeExecutionNumber'],
    ])
  })

  it('rejects a 404 as the typed shared not-found problem', async () => {
    const { historyClient } = answering(
      json(
        {
          type: 'urn:devalente:problem:not-found',
          title: 'The requested resource was not found.',
          status: 404,
          detail: 'See the errors property for details.',
          traceId: '00-abc-def-00',
          errors: [{ code: 'projects.not_found', detail: 'This project does not exist.', pointer: null, parameters: null }],
        },
        404,
      ),
    )

    const refusal = await historyClient.getProjectRunHistory('project-1', null, null).then(
      () => null,
      (caught: unknown) => caught as ApiProblemDetails,
    )

    const problem = refusal as ApiProblemDetails
    expect(problem).toBeInstanceOf(ApiProblemDetails)
    expect(problem.status).toBe(404)
    expect(problem.errors?.[0].code).toBe('projects.not_found')
  })

  it('encodes the project identifier instead of splicing it into the path', async () => {
    const { historyClient, fetch } = answering(json({ projectId: 'x', entries: [], hasMore: false }))

    await historyClient.getProjectRunHistory('p/../x?y=1', null, null)

    const [url] = fetch.mock.calls[0] as unknown as [string]
    expect(url).toBe('http://host.invalid/api/projects/p%2F..%2Fx%3Fy%3D1/run-history')
  })
})
