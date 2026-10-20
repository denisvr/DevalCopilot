import { afterEach, describe, expect, it, vi } from 'vitest'
import { WorkspaceExecutionWireClient } from '../features/cockpit/workspaceExecutionWire'
import { toWorkspaceExecutionReading } from '../features/cockpit/workspaceExecution'
import { PROJECT_ID, WORKSPACE_ID, idleWire, wireFor } from '../features/cockpit/workspaceExecutionFixture'
import { workspaceExecutionStatusClient } from './clients'
import { ApiException, ApiProblemDetails, GetWorkspaceExecutionStatusEndpointClient, GetWorkspaceExecutionStatusResponse } from './generated/api-client'

// The workspace execution status read (ADR-0057): the generated operation coerces explicit nulls, drops unknown members and rebuilds the
// graph, so the cockpit's factory is a parsed-body facade over it, wired to the launch session's base URL and the authenticated transport
// exactly as every other operation is. A clearly fictitious session is used.
vi.mock('./session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

const answer = (text: string, status: number) => new Response(text, { status, headers: { 'Content-Type': 'application/json' } })

afterEach(() => vi.restoreAllMocks())

const problem = (status: number, code: string) => ({
  type: 'urn:devalente:problem:validation',
  title: 'fixed',
  status,
  detail: 'See the errors property for details.',
  traceId: '00-abc-def-00',
  errors: [{ code, detail: 'fixed', pointer: null, parameters: null }],
})

describe('the generated workspace execution operation', () => {
  it('rebuilds the graph: an unknown member vanishes, which is why the decoder reads the raw body', () => {
    const body = { ...idleWire(), extra: 'kept' }

    const generated = GetWorkspaceExecutionStatusResponse.fromJS(body)

    expect(JSON.stringify(generated.toJSON())).not.toContain('extra')
    expect(toWorkspaceExecutionReading(body, PROJECT_ID)).toBeNull()
  })

  it('answers with the generated model, so only the facade can show an explicit null from a missing key', async () => {
    const fetch = vi.fn(() => Promise.resolve(answer(JSON.stringify(wireFor('NoWorkspace')), 200)))

    const generated = await new GetWorkspaceExecutionStatusEndpointClient('http://host.invalid', { fetch }).getWorkspaceExecutionStatus(PROJECT_ID)

    expect(generated).toBeInstanceOf(GetWorkspaceExecutionStatusResponse)
  })
})

describe('workspaceExecutionStatusClient', () => {
  it('is the facade over the generated operation and sends one authenticated bodyless, queryless GET for exactly the project', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(idleWire()), 200))
    const client = workspaceExecutionStatusClient()

    expect(client).toBeInstanceOf(WorkspaceExecutionWireClient)
    expect(client).toBeInstanceOf(GetWorkspaceExecutionStatusEndpointClient)
    await client.readWorkspaceExecutionStatus(PROJECT_ID)

    expect(fetch).toHaveBeenCalledTimes(1)
    const [url, init] = fetch.mock.calls[0]
    expect(url).toBe(`http://127.0.0.1:5099/api/projects/${PROJECT_ID}/workspace-execution-status`)
    expect(init?.method).toBe('GET')
    expect(init?.body).toBeUndefined()
    const headers = new Headers(init?.headers)
    expect(headers.get('Authorization')).toBe('Bearer not-a-real-session')
    expect(headers.get('Accept')).toBe('application/json')
    expect(String(url)).not.toContain('?')
    expect(String(url)).not.toContain('not-a-real-session')
  })

  it("carries its own AbortSignal to the actual authenticated request, so aborting it cancels that request", async () => {
    const fetch = vi.spyOn(window, 'fetch').mockImplementation((_url, init) => new Promise<Response>((_resolve, reject) => {
      init?.signal?.addEventListener('abort', () => reject(new DOMException('The operation was aborted.', 'AbortError')), { once: true })
    }))
    const controller = new AbortController()

    const read = workspaceExecutionStatusClient(controller.signal).readWorkspaceExecutionStatus(PROJECT_ID)
    controller.abort()
    const caught = await read.catch((error: unknown) => error)

    expect(fetch).toHaveBeenCalledTimes(1)
    const init = fetch.mock.calls[0][1]
    expect(init?.signal).toBe(controller.signal)
    expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer not-a-real-session')
    expect((caught as DOMException).name).toBe('AbortError')
  })

  it('sends no signal when none is given, exactly as before', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(idleWire()), 200))

    await workspaceExecutionStatusClient().readWorkspaceExecutionStatus(PROJECT_ID)

    expect(fetch.mock.calls[0][1]?.signal).toBeUndefined()
  })

  it('hands on the parsed body itself, keeping the explicit nulls and unknown members the generated model would lose', async () => {
    const body = { ...wireFor('NoWorkspace'), extra: 'kept' }
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(body), 200))

    const read = await workspaceExecutionStatusClient().readWorkspaceExecutionStatus(PROJECT_ID)

    expect(read).toEqual(body)
    expect(read).not.toBeInstanceOf(GetWorkspaceExecutionStatusResponse)
    expect(toWorkspaceExecutionReading(read, PROJECT_ID)).toBeNull()
  })

  it('carries an exact answer, with its explicit null workspace and reason, through to the decoder', async () => {
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(wireFor('NoWorkspace')), 200))

    const read = (await workspaceExecutionStatusClient().readWorkspaceExecutionStatus(PROJECT_ID)) as Record<string, unknown>

    expect(read.workspaceId).toBeNull()
    expect(read.reasonCode).toBe('workspace_not_prepared')
    expect(toWorkspaceExecutionReading(read, PROJECT_ID)?.status).toBe('NoWorkspace')
  })

  it('keeps a missing key missing, so the decoder can tell it from an explicit null', async () => {
    const { reasonCode: _removed, ...withoutReason } = idleWire()
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(withoutReason), 200))

    const read = await workspaceExecutionStatusClient().readWorkspaceExecutionStatus(PROJECT_ID)

    expect(Object.hasOwn(read as object, 'reasonCode')).toBe(false)
    expect(toWorkspaceExecutionReading(read, PROJECT_ID)).toBeNull()
  })

  it('carries a validated answer for the workspace of the host', async () => {
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(wireFor('VerificationActive')), 200))

    const read = await workspaceExecutionStatusClient().readWorkspaceExecutionStatus(PROJECT_ID)

    expect(toWorkspaceExecutionReading(read, PROJECT_ID)).toMatchObject({ status: 'VerificationActive', workspaceId: WORKSPACE_ID })
  })

  it.each(['', 'not json', '{"status":'])('resolves an empty or non-JSON success body %j as undefined, which the decoder refuses', async (text) => {
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(text, 200))

    const read = await workspaceExecutionStatusClient().readWorkspaceExecutionStatus(PROJECT_ID)

    expect(read).toBeUndefined()
    expect(toWorkspaceExecutionReading(read, PROJECT_ID)).toBeNull()
  })

  it.each([
    [400, 'workspace_execution.query_not_allowed'],
    [400, 'workspace_execution.body_not_allowed'],
    [404, 'projects.not_found'],
  ])('rejects the declared %s (%s) as the typed shared problem with its fixed code after one request', async (status, code) => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(problem(status, code)), status))

    const caught = await workspaceExecutionStatusClient()
      .readWorkspaceExecutionStatus(PROJECT_ID)
      .catch((error: unknown) => error)

    expect(caught).toBeInstanceOf(ApiProblemDetails)
    expect((caught as ApiProblemDetails).status).toBe(status)
    expect((caught as ApiProblemDetails).errors?.map((error) => error.code)).toEqual([code])
    expect(fetch).toHaveBeenCalledTimes(1)
  })

  it.each([401, 500])('rejects an undeclared %s answer with the generated ApiException after one request', async (status) => {
    const fetch = vi.spyOn(window, 'fetch').mockResolvedValue(answer('{}', status))

    const caught = await workspaceExecutionStatusClient()
      .readWorkspaceExecutionStatus(PROJECT_ID)
      .catch((error: unknown) => error)

    expect(ApiException.isApiException(caught)).toBe(true)
    expect((caught as ApiException).status).toBe(status)
    expect(fetch).toHaveBeenCalledTimes(1)
  })
})
