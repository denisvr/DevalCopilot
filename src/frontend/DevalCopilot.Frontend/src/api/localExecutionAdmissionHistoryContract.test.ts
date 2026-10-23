import { afterEach, describe, expect, it, vi } from 'vitest'
import { LocalExecutionAdmissionHistoryWireClient } from '../features/cockpit/localExecutionInspectionWire'
import { toLocalExecutionAdmissionHistory } from '../features/cockpit/localExecutionAdmissionHistory'
import { RUN, notQueuedHistoryFor, recordedHistoryFor } from '../features/cockpit/localExecutionAdmissionHistoryFixture'
import { localExecutionAdmissionHistoryClient } from './clients'
import { GetLocalExecutionAdmissionHistoryEndpointClient, GetLocalExecutionAdmissionHistoryResponse } from './generated/api-client'

// The recorded admission-ledger read (ADR-0063): the factory is a parsed-body facade over the generated operation, wired to the launch
// session base URL and the authenticated transport. A clearly fictitious session is used.
vi.mock('./session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

const answer = (text: string, status: number) => new Response(text, { status, headers: { 'Content-Type': 'application/json' } })

afterEach(() => vi.restoreAllMocks())

describe('the local execution admission history client', () => {
  it('is the facade over the generated operation', () => {
    expect(localExecutionAdmissionHistoryClient()).toBeInstanceOf(LocalExecutionAdmissionHistoryWireClient)
    expect(localExecutionAdmissionHistoryClient()).toBeInstanceOf(GetLocalExecutionAdmissionHistoryEndpointClient)
  })

  it('sends one authenticated bodyless, queryless GET to exactly the documented route', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockImplementation(() => Promise.resolve(answer('{}', 200)))

    await localExecutionAdmissionHistoryClient().readAdmissions('run-1')

    expect(fetch.mock.calls.map(([url]) => url)).toEqual(['http://127.0.0.1:5099/api/runs/run-1/local-execution-sequence/inspection/admissions'])
    const [url, init] = fetch.mock.calls[0]
    expect(init?.method).toBe('GET')
    expect(init?.body).toBeUndefined()
    expect(String(url)).not.toContain('?')
    expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer not-a-real-session')
    expect(String(url)).not.toContain('not-a-real-session')
  })

  it('hands on the parsed body itself, keeping explicit nulls and unknown members the generated model would drop', async () => {
    const body = { ...recordedHistoryFor(RUN), extra: 'kept' }
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(body), 200))

    const read = await localExecutionAdmissionHistoryClient().readAdmissions(RUN)

    expect(read).toEqual(body)
    expect(read).not.toBeInstanceOf(GetLocalExecutionAdmissionHistoryResponse)
    expect(toLocalExecutionAdmissionHistory(read, RUN)).toBeNull()
    const plain = JSON.parse(JSON.stringify(notQueuedHistoryFor(RUN)))
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(plain), 200))
    expect(toLocalExecutionAdmissionHistory(await localExecutionAdmissionHistoryClient().readAdmissions(RUN), RUN)).toEqual({ status: 'NotQueued' })
  })

  it('resolves an empty or non-JSON 200 body as undefined, which the decoder refuses', async () => {
    vi.spyOn(window, 'fetch').mockResolvedValueOnce(answer('', 200)).mockResolvedValueOnce(answer('<html>', 200))

    expect(await localExecutionAdmissionHistoryClient().readAdmissions(RUN)).toBeUndefined()
    expect(await localExecutionAdmissionHistoryClient().readAdmissions(RUN)).toBeUndefined()
    expect(toLocalExecutionAdmissionHistory(undefined, RUN)).toBeNull()
  })

  it('leaves every non-200 answer to the generated operation, as an ApiProblemDetails carrying the status', async () => {
    const problem = JSON.stringify({ type: 'urn:devalente:problem:not-found', status: 404, errors: [{ code: 'local_execution_inspection.not_found' }] })
    vi.spyOn(window, 'fetch').mockImplementation(() => Promise.resolve(answer(problem, 404)))

    await expect(localExecutionAdmissionHistoryClient().readAdmissions(RUN)).rejects.toMatchObject({ status: 404 })
  })
})
