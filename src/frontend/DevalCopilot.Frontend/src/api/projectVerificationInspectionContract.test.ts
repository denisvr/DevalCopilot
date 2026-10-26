import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  ProjectVerificationHistoryWireClient,
  ProjectVerificationInspectionWireClient,
  ProjectVerificationWindowWireClient,
} from '../features/cockpit/projectVerificationInspectionWire'
import { toProjectVerificationHistory } from '../features/cockpit/projectVerificationInspection'
import {
  PROJECT,
  executionIdFor,
  inspectionFor,
  pageFor,
  windowFor,
} from '../features/cockpit/projectVerificationInspectionFixture'
import {
  projectVerificationHistoryClient,
  projectVerificationInspectionClient,
  projectVerificationWindowClient,
} from './clients'
import {
  ApiProblemDetails,
  GetProjectVerificationHistoryEndpointClient,
  GetProjectVerificationHistoryResponse,
  GetProjectVerificationInspectionEndpointClient,
  GetProjectVerificationOutputWindowEndpointClient,
} from './generated/api-client'

// The recorded project verification reads (ADR-0069): the factories are parsed-body facades over the generated operations, wired to the
// launch session base URL and the authenticated transport. A clearly fictitious session is used.
vi.mock('./session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

const BASE = 'http://127.0.0.1:5099'
const answer = (text: string, status: number) => new Response(text, { status, headers: { 'Content-Type': 'application/json' } })

afterEach(() => vi.restoreAllMocks())

describe('the project verification inspection clients', () => {
  it('are the facades over the generated operations', () => {
    expect(projectVerificationHistoryClient()).toBeInstanceOf(ProjectVerificationHistoryWireClient)
    expect(projectVerificationHistoryClient()).toBeInstanceOf(GetProjectVerificationHistoryEndpointClient)
    expect(projectVerificationInspectionClient()).toBeInstanceOf(ProjectVerificationInspectionWireClient)
    expect(projectVerificationInspectionClient()).toBeInstanceOf(GetProjectVerificationInspectionEndpointClient)
    expect(projectVerificationWindowClient()).toBeInstanceOf(ProjectVerificationWindowWireClient)
    expect(projectVerificationWindowClient()).toBeInstanceOf(GetProjectVerificationOutputWindowEndpointClient)
  })

  it('send one authenticated bodyless GET each, to exactly the documented routes and queries', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockImplementation(() => Promise.resolve(answer('{}', 200)))
    const execution = executionIdFor(7)

    await projectVerificationHistoryClient().readPage(PROJECT, null)
    await projectVerificationHistoryClient().readPage(PROJECT, 26)
    await projectVerificationInspectionClient().readExecution(PROJECT, execution)
    await projectVerificationWindowClient().readWindow(PROJECT, execution, 'stdout', 0, 16384)
    await projectVerificationWindowClient().readWindow(PROJECT, execution, 'stderr', 16384, 64)

    expect(fetch.mock.calls.map(([url]) => url)).toEqual([
      `${BASE}/api/projects/${PROJECT}/verification-history`,
      `${BASE}/api/projects/${PROJECT}/verification-history?beforeExecutionNumber=26`,
      `${BASE}/api/projects/${PROJECT}/verification-history/${execution}`,
      `${BASE}/api/projects/${PROJECT}/verification-history/${execution}/artifact-window/stdout?fromOffset=0&maxBytes=16384`,
      `${BASE}/api/projects/${PROJECT}/verification-history/${execution}/artifact-window/stderr?fromOffset=16384&maxBytes=64`,
    ])
    for (const [url, init] of fetch.mock.calls) {
      expect(init?.method).toBe('GET')
      expect(init?.body).toBeUndefined()
      expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer not-a-real-session')
      expect(String(url)).not.toContain('not-a-real-session')
    }
  })

  it('hand on the parsed body itself, keeping explicit nulls and unknown members the generated model would rebuild or drop', async () => {
    const body = { ...pageFor(5, 3), extra: 'kept' }
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(body), 200))

    const read = await projectVerificationHistoryClient().readPage(PROJECT, null)

    expect(read).toEqual(body)
    expect(read).not.toBeInstanceOf(GetProjectVerificationHistoryResponse)
    expect(toProjectVerificationHistory(read, { projectId: PROJECT, beforeExecutionNumber: null })).toBeNull()

    const inspection = inspectionFor(7, {}, { exitCode: null, outcome: null, status: 'Interrupted', completionFingerprintSha256: null })
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(inspection), 200))
    expect(await projectVerificationInspectionClient().readExecution(PROJECT, executionIdFor(7))).toEqual(inspection)

    const window7 = windowFor(7, 'stdout')
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(window7), 200))
    expect(await projectVerificationWindowClient().readWindow(PROJECT, executionIdFor(7), 'stdout', 0, 64)).toEqual(window7)
  })

  it('resolve an empty or non-JSON 200 body as undefined, which the decoders refuse', async () => {
    vi.spyOn(window, 'fetch')
      .mockResolvedValueOnce(answer('', 200))
      .mockResolvedValueOnce(answer('<html>', 200))
      .mockResolvedValueOnce(answer('not json', 200))

    expect(await projectVerificationHistoryClient().readPage(PROJECT, null)).toBeUndefined()
    expect(await projectVerificationInspectionClient().readExecution(PROJECT, executionIdFor(7))).toBeUndefined()
    expect(await projectVerificationWindowClient().readWindow(PROJECT, executionIdFor(7), 'stdout', 0, 64)).toBeUndefined()
    expect(toProjectVerificationHistory(undefined, { projectId: PROJECT, beforeExecutionNumber: null })).toBeNull()
  })

  it('leave every non-200 answer to the generated operation, as an ApiProblemDetails carrying the status and the fixed code', async () => {
    const problem = JSON.stringify({
      type: 'urn:devalente:problem:not-found',
      status: 404,
      errors: [{ code: 'project_verification_inspection.not_found' }],
    })
    vi.spyOn(window, 'fetch').mockImplementation(() => Promise.resolve(answer(problem, 404)))

    for (const read of [
      () => projectVerificationHistoryClient().readPage(PROJECT, null),
      () => projectVerificationInspectionClient().readExecution(PROJECT, executionIdFor(7)),
      () => projectVerificationWindowClient().readWindow(PROJECT, executionIdFor(7), 'stdout', 0, 64),
    ]) {
      const caught = await read().catch((error: unknown) => error)
      expect(caught).toBeInstanceOf(ApiProblemDetails)
      expect(caught).toMatchObject({ status: 404 })
    }

    vi.spyOn(window, 'fetch').mockImplementation(() => Promise.resolve(answer('{"status":400,"errors":[]}', 400)))
    await expect(projectVerificationHistoryClient().readPage(PROJECT, 0)).rejects.toMatchObject({ status: 400 })
  })

  it('carry their own AbortSignal into the actual request, so ending the visit cancels the transport', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockImplementation((_url, init) => {
      return new Promise<Response>((_resolve, reject) => {
        init?.signal?.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')))
      })
    })
    const controller = new AbortController()
    const pending = projectVerificationHistoryClient(controller.signal).readPage(PROJECT, null)

    expect(fetch.mock.calls[0][1]?.signal).toBe(controller.signal)
    controller.abort()
    await expect(pending).rejects.toMatchObject({ name: 'AbortError' })

    fetch.mockClear()
    fetch.mockImplementation(() => Promise.resolve(answer('{}', 200)))
    await projectVerificationWindowClient().readWindow(PROJECT, executionIdFor(7), 'stdout', 0, 64).catch(() => undefined)
    expect(fetch.mock.calls[0][1]?.signal).toBeUndefined()
  })
})
