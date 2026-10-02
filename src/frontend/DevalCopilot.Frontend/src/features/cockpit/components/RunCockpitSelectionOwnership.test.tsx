import { Profiler } from 'react'
import { act, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { runCockpitClient, runEventsClient } from '../../../api/clients'
import { createRunNotificationConnection } from '../../../api/runNotifications'
import {
  GetRunCockpitResponse,
  ParticipantIdentityResponse,
  StageMapEntryResponse,
} from '../../../api/generated/api-client'
import { RunCockpitView } from './RunCockpitView'

// Every other client of the cockpit is unreachable here: its panels settle into their own error
// states, which is irrelevant to which run the header, stages and actions describe.
vi.mock('../../../api/clients', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../api/clients')>()
  const unreachable = () => new Proxy({}, { get: () => () => Promise.reject(new Error('unavailable')) })
  const clients = Object.fromEntries(
    Object.entries(actual)
      .filter(([name, value]) => name.endsWith('Client') && typeof value === 'function')
      .map(([name]) => [name, vi.fn(unreachable)]),
  )
  return { ...actual, ...clients }
})
vi.mock('../../../api/runNotifications', () => ({ createRunNotificationConnection: vi.fn() }))

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

const RUNS = {
  'run-a': { objective: 'Migrate the ledger to the new schema', project: 'Ledger service', stage: 'Plan review gate' },
  'run-b': { objective: 'Harden the export pipeline', project: 'Export service', stage: 'Implementation sweep' },
} as const
type RunId = keyof typeof RUNS

function cockpit(id: RunId) {
  return new GetRunCockpitResponse({
    runId: id,
    projectId: `project-of-${id}`,
    projectName: RUNS[id].project,
    executionNumber: id === 'run-a' ? 3 : 8,
    objective: RUNS[id].objective,
    lifecycle: 'Running',
    stage: 'Plan',
    executionMode: 'ManualAgent',
    activeParticipant: new ParticipantIdentityResponse({ kind: 'Agent', role: 'Planner', provider: 'Codex' }),
    autonomousDurationSeconds: 1,
    latestSequence: 0,
    stageMap: [new StageMapEntryResponse({ stage: RUNS[id].stage, isCompleted: false, isActive: true })],
    canPause: false,
    canStop: false,
  })
}

// Profiler fires after every commit of its subtree, once the DOM is mutated and before paint, so a frame
// that shows the previous run cannot hide behind a later, correct one.
const frames: string[] = []
const recordFrame = () => {
  frames.push(document.body.textContent ?? '')
}

function view(id: RunId) {
  return (
    <>
      <Profiler id="cockpit" onRender={recordFrame}>
        <RunCockpitView runId={id} />
      </Profiler>
    </>
  )
}

function expectNoFrameOf(id: RunId, from: number) {
  expect(frames.length).toBeGreaterThan(from)
  for (const frame of frames.slice(from)) {
    expect(frame).not.toContain(RUNS[id].objective)
    expect(frame).not.toContain(RUNS[id].project)
    expect(frame).not.toContain(RUNS[id].stage)
  }
}

const flush = () => act(async () => {})

describe('RunCockpitView run selection ownership', () => {
  let cockpitAnswers: Partial<Record<RunId, ReturnType<typeof deferred<GetRunCockpitResponse>>>>

  beforeEach(() => {
    vi.clearAllMocks()
    frames.length = 0
    cockpitAnswers = {}
    vi.mocked(createRunNotificationConnection).mockImplementation(
      () =>
        ({
          on: vi.fn(),
          onreconnecting: vi.fn(),
          onreconnected: vi.fn(),
          onclose: vi.fn(),
          start: vi.fn(() => new Promise(() => {})),
          stop: vi.fn(() => Promise.resolve()),
        }) as unknown as ReturnType<typeof createRunNotificationConnection>,
    )
    vi.mocked(runEventsClient).mockReturnValue({ getRunEvents: vi.fn().mockResolvedValue([]) } as unknown as ReturnType<
      typeof runEventsClient
    >)
    vi.mocked(runCockpitClient).mockReturnValue({
      getRunCockpit: vi.fn((id: RunId) => (cockpitAnswers[id] ??= deferred<GetRunCockpitResponse>()).promise),
    } as unknown as ReturnType<typeof runCockpitClient>)
  })

  it('shows no header, stage or action of run A while B loads, fails or answers late, including on return (A to B to A)', async () => {
    cockpitAnswers['run-a'] = deferred()
    cockpitAnswers['run-a'].resolve(cockpit('run-a'))
    const { rerender } = render(view('run-a'))
    await flush()
    expect(screen.getByRole('heading', { name: RUNS['run-a'].objective })).toBeInTheDocument()
    expect(screen.getByText(RUNS['run-a'].stage)).toBeInTheDocument()

    // B is pending: its own loading state, never A's header, stages or Agent actions.
    let mark = frames.length
    rerender(view('run-b'))
    expectNoFrameOf('run-a', mark)
    expect(screen.getByText('Loading run…')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: RUNS['run-a'].objective })).toBeNull()
    expect(screen.queryByText(RUNS['run-a'].stage)).toBeNull()
    expect(screen.queryByRole('button', { name: /Request/ })).toBeNull()

    // B fails: its own error, still nothing of A.
    await act(async () => {
      cockpitAnswers['run-b']!.reject(new Error('Run B could not be loaded.'))
    })
    expect(screen.getByText('Run B could not be loaded.')).toBeInTheDocument()
    expectNoFrameOf('run-a', mark)

    // Back to A: a new lifetime, loading again; the earlier loaded A is not resurrected, and the late
    // completion of the replaced A fetch applies to nothing.
    delete cockpitAnswers['run-a']
    mark = frames.length
    rerender(view('run-a'))
    expect(screen.getByText('Loading run…')).toBeInTheDocument()
    expectNoFrameOf('run-b', mark)
    expect(screen.queryByText('Run B could not be loaded.')).toBeNull()
    expect(screen.queryByRole('heading', { name: RUNS['run-a'].objective })).toBeNull()
    await act(async () => {
      cockpitAnswers['run-a']!.resolve(cockpit('run-a'))
    })
    expect(screen.getByRole('heading', { name: RUNS['run-a'].objective })).toBeInTheDocument()
  })

  it('shows B only when B answers, and ignores a late answer for A that arrives afterwards', async () => {
    const { rerender } = render(view('run-a'))
    await flush()
    const lateA = cockpitAnswers['run-a']!
    const mark = frames.length
    rerender(view('run-b'))
    await act(async () => {
      cockpitAnswers['run-b']!.resolve(cockpit('run-b'))
    })
    expect(screen.getByRole('heading', { name: RUNS['run-b'].objective })).toBeInTheDocument()
    expect(screen.getByText(RUNS['run-b'].stage)).toBeInTheDocument()

    await act(async () => {
      lateA.resolve(cockpit('run-a'))
    })
    expectNoFrameOf('run-a', mark)
    expect(screen.getByRole('heading', { name: RUNS['run-b'].objective })).toBeInTheDocument()
  })

  it('does not present a response that names another run as the selected cockpit', async () => {
    const { rerender } = render(view('run-a'))
    await flush()
    rerender(view('run-b'))
    const mark = frames.length
    await act(async () => {
      cockpitAnswers['run-b']!.resolve(cockpit('run-a'))
    })
    expectNoFrameOf('run-a', mark)
    expect(screen.queryByRole('heading', { name: RUNS['run-a'].objective })).toBeNull()
    expect(screen.getByText('The host answered with a different run than the one selected.')).toBeInTheDocument()
  })
})
