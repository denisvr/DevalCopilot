import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  LocalExecutionInspectionIndexWireClient,
  LocalExecutionInspectionStepWireClient,
  LocalExecutionInspectionWindowWireClient,
} from '../features/cockpit/localExecutionInspectionWire'
import { toLocalExecutionInspectionIndex } from '../features/cockpit/localExecutionInspection'
import { RUN, recordedIndexFor, stepIdFor } from '../features/cockpit/localExecutionInspectionFixture'
import {
  localExecutionInspectionIndexClient,
  localExecutionInspectionStepClient,
  localExecutionInspectionWindowClient,
} from './clients'
import {
  GetLocalExecutionSequenceInspectionEndpointClient,
  GetLocalExecutionSequenceInspectionResponse,
  GetLocalExecutionStepInspectionEndpointClient,
  GetLocalExecutionVerificationOutputWindowEndpointClient,
} from './generated/api-client'

// The recorded local execution inspection reads (ADR-0061): the factories are parsed-body facades over the generated operations, wired to
// the launch session base URL and the authenticated transport. A clearly fictitious session is used.
vi.mock('./session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

const answer = (text: string, status: number) => new Response(text, { status, headers: { 'Content-Type': 'application/json' } })

afterEach(() => vi.restoreAllMocks())

describe('the local execution inspection clients', () => {
  it('are the facades over the generated operations', () => {
    expect(localExecutionInspectionIndexClient()).toBeInstanceOf(LocalExecutionInspectionIndexWireClient)
    expect(localExecutionInspectionIndexClient()).toBeInstanceOf(GetLocalExecutionSequenceInspectionEndpointClient)
    expect(localExecutionInspectionStepClient()).toBeInstanceOf(LocalExecutionInspectionStepWireClient)
    expect(localExecutionInspectionStepClient()).toBeInstanceOf(GetLocalExecutionStepInspectionEndpointClient)
    expect(localExecutionInspectionWindowClient()).toBeInstanceOf(LocalExecutionInspectionWindowWireClient)
    expect(localExecutionInspectionWindowClient()).toBeInstanceOf(GetLocalExecutionVerificationOutputWindowEndpointClient)
  })

  it('send one authenticated bodyless GET each, to exactly the documented routes and queries', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockImplementation(() => Promise.resolve(answer('{}', 200)))

    await localExecutionInspectionIndexClient().readIndex('run-1')
    await localExecutionInspectionStepClient().readStep('run-1', 'step-1')
    await localExecutionInspectionWindowClient().readWindow('run-1', 'step-1', 'stderr', 5, 64)

    expect(fetch.mock.calls.map(([url]) => url)).toEqual([
      'http://127.0.0.1:5099/api/runs/run-1/local-execution-sequence/inspection',
      'http://127.0.0.1:5099/api/runs/run-1/local-execution-sequence/inspection/steps/step-1',
      'http://127.0.0.1:5099/api/runs/run-1/local-execution-sequence/inspection/steps/step-1/artifact-window/stderr?fromOffset=5&maxBytes=64',
    ])
    for (const [url, init] of fetch.mock.calls) {
      expect(init?.method).toBe('GET')
      expect(init?.body).toBeUndefined()
      expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer not-a-real-session')
      expect(String(url)).not.toContain('not-a-real-session')
    }
  })

  it('hand on the parsed body itself, keeping explicit nulls the generated model would rebuild', async () => {
    const body = { ...recordedIndexFor(RUN, [stepFor0()]), extra: 'kept' }
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(body), 200))

    const read = await localExecutionInspectionIndexClient().readIndex(RUN)

    expect(read).toEqual(body)
    expect(read).not.toBeInstanceOf(GetLocalExecutionSequenceInspectionResponse)
    expect(toLocalExecutionInspectionIndex(read, RUN)).toBeNull()
  })

  it('resolve an empty or non-JSON 200 body as undefined, which the decoder refuses', async () => {
    vi.spyOn(window, 'fetch').mockResolvedValueOnce(answer('', 200)).mockResolvedValueOnce(answer('<html>', 200))

    expect(await localExecutionInspectionStepClient().readStep(RUN, stepIdFor(0))).toBeUndefined()
    expect(await localExecutionInspectionIndexClient().readIndex(RUN)).toBeUndefined()
    expect(toLocalExecutionInspectionIndex(undefined, RUN)).toBeNull()
  })

  it('leave every non-200 answer to the generated operation, as an ApiException carrying the status', async () => {
    const problem = JSON.stringify({ type: 'urn:devalente:problem:not-found', status: 404, errors: [{ code: 'local_execution_inspection.not_found' }] })
    vi.spyOn(window, 'fetch').mockImplementation(() => Promise.resolve(answer(problem, 404)))

    await expect(localExecutionInspectionWindowClient().readWindow(RUN, 's', 'stdout', 0, 64)).rejects.toMatchObject({ status: 404 })
  })
})

function stepFor0() {
  return recordedIndexFor().steps[0]
}
