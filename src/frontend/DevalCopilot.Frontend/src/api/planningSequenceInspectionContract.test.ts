import { afterEach, describe, expect, it, vi } from 'vitest'
import { PlanningSequenceInspectionIndexWireClient, PlanningSequenceInspectionStepWireClient } from '../features/cockpit/planningSequenceInspectionWire'
import { toPlanningInspectionIndex } from '../features/cockpit/planningSequenceInspection'
import { RUN, recordedIndexFor, stepIdFor } from '../features/cockpit/planningSequenceInspectionFixture'
import { planningSequenceInspectionIndexClient, planningSequenceInspectionStepClient } from './clients'
import {
  GetPlanningSequenceInspectionEndpointClient,
  GetPlanningSequenceInspectionResponse,
  GetPlanningStepInspectionEndpointClient,
} from './generated/api-client'

// The recorded planning sequence inspection reads (ADR-0068): the factories are parsed-body facades over the generated operations, wired to
// the launch session base URL and the authenticated transport. A clearly fictitious session is used.
vi.mock('./session', () => ({ getLaunchSession: () => ({ baseUrl: 'http://127.0.0.1:5099', secret: 'not-a-real-session' }) }))

const answer = (text: string, status: number) => new Response(text, { status, headers: { 'Content-Type': 'application/json' } })

afterEach(() => vi.restoreAllMocks())

describe('the planning sequence inspection clients', () => {
  it('are the facades over the generated operations', () => {
    expect(planningSequenceInspectionIndexClient()).toBeInstanceOf(PlanningSequenceInspectionIndexWireClient)
    expect(planningSequenceInspectionIndexClient()).toBeInstanceOf(GetPlanningSequenceInspectionEndpointClient)
    expect(planningSequenceInspectionStepClient()).toBeInstanceOf(PlanningSequenceInspectionStepWireClient)
    expect(planningSequenceInspectionStepClient()).toBeInstanceOf(GetPlanningStepInspectionEndpointClient)
  })

  it('send one authenticated bodyless GET each, to exactly the documented routes and no query', async () => {
    const fetch = vi.spyOn(window, 'fetch').mockImplementation(() => Promise.resolve(answer('{}', 200)))

    await planningSequenceInspectionIndexClient().readIndex('run-1')
    await planningSequenceInspectionStepClient().readStep('run-1', 'step-1')

    expect(fetch.mock.calls.map(([url]) => url)).toEqual([
      'http://127.0.0.1:5099/api/runs/run-1/planning-sequence/inspection',
      'http://127.0.0.1:5099/api/runs/run-1/planning-sequence/inspection/steps/step-1',
    ])
    for (const [url, init] of fetch.mock.calls) {
      expect(init?.method).toBe('GET')
      expect(init?.body).toBeUndefined()
      expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer not-a-real-session')
      expect(String(url)).not.toContain('not-a-real-session')
      expect(String(url)).not.toContain('?')
    }
  })

  it('hand on the parsed body itself, keeping explicit nulls the generated model would rebuild', async () => {
    const body = { ...recordedIndexFor(), extra: 'kept' }
    vi.spyOn(window, 'fetch').mockResolvedValue(answer(JSON.stringify(body), 200))

    const read = await planningSequenceInspectionIndexClient().readIndex(RUN)

    expect(read).toEqual(body)
    expect(read).not.toBeInstanceOf(GetPlanningSequenceInspectionResponse)
    expect(toPlanningInspectionIndex(read, RUN)).toBeNull()
  })

  it('resolve an empty or non-JSON 200 body as undefined, which the decoder refuses', async () => {
    vi.spyOn(window, 'fetch').mockResolvedValueOnce(answer('', 200)).mockResolvedValueOnce(answer('<html>', 200))

    expect(await planningSequenceInspectionStepClient().readStep(RUN, stepIdFor(0))).toBeUndefined()
    expect(await planningSequenceInspectionIndexClient().readIndex(RUN)).toBeUndefined()
    expect(toPlanningInspectionIndex(undefined, RUN)).toBeNull()
  })

  it('leave every non-200 answer to the generated operation, as an ApiException carrying the status', async () => {
    const problem = JSON.stringify({ type: 'urn:devalente:problem:not-found', status: 404, errors: [{ code: 'planning_sequence_inspection.not_found' }] })
    vi.spyOn(window, 'fetch').mockImplementation(() => Promise.resolve(answer(problem, 404)))

    await expect(planningSequenceInspectionStepClient().readStep(RUN, stepIdFor(0))).rejects.toMatchObject({ status: 404 })
    await expect(planningSequenceInspectionIndexClient().readIndex(RUN)).rejects.toMatchObject({ status: 404 })
  })
})
