import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  projectVerificationHistoryClient,
  projectVerificationInspectionClient,
  projectVerificationWindowClient,
} from '../../../api/clients'
import { ApiProblemDetails } from '../../../api/generated/api-client'
import { VERIFICATION_VIEW_BUDGET_BYTES, VERIFICATION_WINDOW_BYTES } from '../agentAttemptVerification'
import {
  PROJECT,
  executionIdFor,
  guidOf,
  inspectionFor,
  pageFor,
  recordedStreamFor,
  refusedWindowFor,
  unavailableInspectionFor,
  unavailablePageFor,
  windowFor,
} from '../projectVerificationInspectionFixture'
import { useProjectVerificationInspection } from './useProjectVerificationInspection'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  projectVerificationHistoryClient: vi.fn(),
  projectVerificationInspectionClient: vi.fn(),
  projectVerificationWindowClient: vi.fn(),
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

const PROJECT_B = guidOf('p', 2)
const numberOf = (executionId: string): number => {
  for (let number = 1; number <= 200; number += 1) {
    if (executionIdFor(number) === executionId) {
      return number
    }
  }

  throw new Error('unknown execution')
}

interface Handlers {
  page?: (projectId: string, before: number | null) => Promise<unknown>
  execution?: (projectId: string, executionId: string) => Promise<unknown>
  window?: (projectId: string, executionId: string, stream: 'stdout' | 'stderr', from: number, max: number) => Promise<unknown>
}

function install(handlers: Handlers = {}) {
  const pages: { projectId: string; before: number | null; signal?: AbortSignal }[] = []
  const executions: { projectId: string; executionId: string; signal?: AbortSignal }[] = []
  const windows: { projectId: string; executionId: string; stream: string; from: number; max: number; signal?: AbortSignal }[] = []
  vi.mocked(projectVerificationHistoryClient).mockImplementation(
    (signal?: AbortSignal) =>
      ({
        readPage: (projectId: string, before: number | null) => {
          pages.push({ projectId, before, signal })
          return (handlers.page ?? ((id, cursor) => { const first = cursor === null ? 45 : cursor - 1; return Promise.resolve(pageFor(first, Math.min(20, first), {}, id)) }))(projectId, before)
        },
      }) as unknown as ReturnType<typeof projectVerificationHistoryClient>,
  )
  vi.mocked(projectVerificationInspectionClient).mockImplementation(
    (signal?: AbortSignal) =>
      ({
        readExecution: (projectId: string, executionId: string) => {
          executions.push({ projectId, executionId, signal })
          return (handlers.execution ?? ((id, execution) => Promise.resolve(inspectionFor(numberOf(execution), {}, {}, id))))(
            projectId,
            executionId,
          )
        },
      }) as unknown as ReturnType<typeof projectVerificationInspectionClient>,
  )
  vi.mocked(projectVerificationWindowClient).mockImplementation(
    (signal?: AbortSignal) =>
      ({
        readWindow: (projectId: string, executionId: string, stream: 'stdout' | 'stderr', from: number, max: number) => {
          windows.push({ projectId, executionId, stream, from, max, signal })
          return (handlers.window ?? ((id, execution, kind) => Promise.resolve(windowFor(numberOf(execution), kind, {}, id))))(
            projectId,
            executionId,
            stream,
            from,
            max,
          )
        },
      }) as unknown as ReturnType<typeof projectVerificationWindowClient>,
  )
  return { pages, executions, windows }
}

const flush = () => act(async () => undefined)
const notFound = () => new ApiProblemDetails({ status: 404, errors: [], traceId: 'trace' })

function mount(projectId: string | null = PROJECT) {
  const renders: ReturnType<typeof useProjectVerificationInspection>[] = []
  const view = renderHook(
    ({ id }: { id: string | null }) => {
      const value = useProjectVerificationInspection(id)
      renders.push(value)
      return value
    },
    { initialProps: { id: projectId } },
  )
  return { ...view, renders }
}

async function openAndSelect(view: ReturnType<typeof mount>, number = 45) {
  act(() => view.result.current.show())
  await flush()
  act(() => view.result.current.selectExecution(executionIdFor(number)))
  await flush()
}

describe('useProjectVerificationInspection', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })
  afterEach(() => {
    vi.useRealTimers()
  })

  it('reads nothing until the owner opens the region, then reads exactly the newest page', async () => {
    const calls = install()
    const { result } = mount()

    expect(result.current.open).toBe(false)
    expect(calls.pages).toEqual([])

    act(() => result.current.show())
    act(() => result.current.show())
    await flush()

    expect(calls.pages.map(({ projectId, before }) => ({ projectId, before }))).toEqual([{ projectId: PROJECT, before: null }])
    expect(calls.executions).toEqual([])
    expect(calls.windows).toEqual([])
    expect(result.current.page).toMatchObject({ status: 'Recorded', hasMore: true, nextBeforeExecutionNumber: 26 })
    expect(result.current.failure).toBeNull()
    expect(result.current.pending).toBe(false)
  })

  it('does nothing for a null project', async () => {
    const calls = install()
    const { result } = mount(null)

    act(() => result.current.show())
    await flush()

    expect(result.current.open).toBe(false)
    expect(calls.pages).toEqual([])
  })

  it('replaces the page by the older one using the displayed cursor, without accumulating or paging in the background', async () => {
    const calls = install()
    const view = mount()
    act(() => view.result.current.show())
    await flush()

    act(() => view.result.current.older())
    await flush()

    expect(calls.pages.map((call) => call.before)).toEqual([null, 26])
    const page = view.result.current.page
    expect(page?.status === 'Recorded' ? page.items.map((item) => item.executionNumber) : []).toEqual(
      Array.from({ length: 20 }, (_, index) => 25 - index),
    )
    expect(page).toMatchObject({ hasMore: true, nextBeforeExecutionNumber: 6 })

    act(() => view.result.current.older())
    await flush()
    const last = view.result.current.page
    expect(last?.status === 'Recorded' ? last.items.map((item) => item.executionNumber) : []).toEqual([5, 4, 3, 2, 1])
    expect(last).toMatchObject({ hasMore: false, nextBeforeExecutionNumber: null })

    // No older page exists, so another call is a no-op and nothing is read in the background.
    act(() => view.result.current.older())
    await flush()
    expect(calls.pages.map((call) => call.before)).toEqual([null, 26, 6])
  })

  it('ends the selection, its content and the window in the render that starts an older page or a reload, and never flashes them', async () => {
    const calls = install()
    const view = mount()
    await openAndSelect(view)
    act(() => view.result.current.selectStream('stdout'))
    await flush()
    act(() => view.result.current.window.load())
    await flush()
    expect(view.result.current.window.state.status).toBe('loaded')
    const oldKey = view.result.current.pageKey
    const gate = deferred<unknown>()
    view.renders.length = 0
    calls.pages.length = 0
    install({ page: () => gate.promise })

    act(() => view.result.current.older())

    for (const render of view.renders) {
      if (render.pageKey !== oldKey) {
        expect(render.page).toBeNull()
        expect(render.selectedItem).toBeNull()
        expect(render.inspection).toBeNull()
        expect(render.selection).toBeNull()
        expect(render.window.state.status).toBe('idle')
      }
    }

    expect(view.result.current.pending).toBe(true)
    expect(view.result.current.page).toBeNull()
    expect(view.result.current.selectedItem).toBeNull()
    expect(view.result.current.window.state).toEqual({ status: 'idle' })
    gate.resolve(pageFor(25, 20))
    await flush()
    expect(view.result.current.page?.status).toBe('Recorded')

    install()
    act(() => view.result.current.reload())
    expect(view.result.current.page).toBeNull()
    await flush()
    expect(view.result.current.page).toMatchObject({ status: 'Recorded', nextBeforeExecutionNumber: 26 })
  })

  it('reads only the chosen execution metadata and never an output window until a stream is chosen and loaded', async () => {
    const calls = install()
    const view = mount()
    await openAndSelect(view, 44)

    expect(calls.executions.map((call) => call.executionId)).toEqual([executionIdFor(44)])
    expect(calls.windows).toEqual([])
    expect(view.result.current.selectedItem?.executionNumber).toBe(44)
    expect(view.result.current.inspection).toMatchObject({ status: 'Recorded' })

    act(() => view.result.current.selectExecution(guidOf('x', 9)))
    await flush()
    expect(calls.executions).toHaveLength(1)

    // Choosing the already chosen execution keeps its visit and sends nothing.
    act(() => view.result.current.selectExecution(executionIdFor(44)))
    await flush()
    expect(calls.executions).toHaveLength(1)

    act(() => view.result.current.selectStream('stdout'))
    await flush()
    expect(calls.windows).toEqual([])
    expect(view.result.current.selection).toMatchObject({ stream: 'stdout', visit: 1 })

    act(() => view.result.current.window.load())
    await flush()
    expect(calls.windows.map(({ executionId, stream, from, max }) => ({ executionId, stream, from, max }))).toEqual([
      { executionId: executionIdFor(44), stream: 'stdout', from: 0, max: VERIFICATION_WINDOW_BYTES },
    ])
    expect(view.result.current.window.state).toMatchObject({ status: 'loaded', text: 'hello output', isComplete: true })
  })

  it('ignores a stream whose accepted descriptor is not Recorded', async () => {
    install({
      execution: (id, execution) =>
        Promise.resolve(inspectionFor(numberOf(execution), {}, { standardError: { status: 'NotRecorded', artifactId: null, byteLength: null, captureOutcome: null, truncated: null } }, id)),
    })
    const view = mount()
    await openAndSelect(view)

    act(() => view.result.current.selectStream('stderr'))
    await flush()
    expect(view.result.current.selection).toBeNull()
    act(() => view.result.current.selectStream('stdout'))
    await flush()
    expect(view.result.current.selection?.stream).toBe('stdout')
  })

  it('appends a next window and stops at the 256 KiB source-byte view budget with a local limit, never an end', async () => {
    const total = 300_000
    const calls = install({
      execution: (id, execution) =>
        Promise.resolve(inspectionFor(numberOf(execution), {}, { standardOutput: recordedStreamFor(numberOf(execution), 'stdout', { byteLength: total }) }, id)),
      window: (id, execution, stream, from, max) =>
        Promise.resolve({
          ...windowFor(numberOf(execution), stream, {}, id),
          artifact: recordedStreamFor(numberOf(execution), 'stdout', { byteLength: total }),
          text: 'a'.repeat(max),
          nextOffset: from + max,
          totalLength: total,
        }),
    })
    const view = mount()
    await openAndSelect(view)
    act(() => view.result.current.selectStream('stdout'))
    await flush()

    for (let index = 0; index < 40 && calls.windows.length < 16; index += 1) {
      act(() => view.result.current.window.load())
      await flush()
    }

    expect(calls.windows.map((call) => call.from)).toEqual(Array.from({ length: 16 }, (_, index) => index * VERIFICATION_WINDOW_BYTES))
    expect(calls.windows.every((call) => call.max === VERIFICATION_WINDOW_BYTES)).toBe(true)
    expect(view.result.current.window.state).toMatchObject({
      status: 'loaded',
      nextOffset: VERIFICATION_VIEW_BUDGET_BYTES,
      isComplete: false,
      viewLimitReached: true,
    })
    act(() => view.result.current.window.load())
    await flush()
    expect(calls.windows).toHaveLength(16)
  })

  it('keeps unknown truncation, an empty output and each refused status distinct', async () => {
    install({
      execution: (id, execution) =>
        Promise.resolve(
          inspectionFor(numberOf(execution), {}, {
            standardOutput: recordedStreamFor(numberOf(execution), 'stdout', { byteLength: 0, captureOutcome: 'RecoveredAfterHostInterruption', truncated: null }),
          }, id),
        ),
      window: (id, execution) =>
        Promise.resolve({
          ...windowFor(numberOf(execution), 'stdout', {}, id),
          artifact: recordedStreamFor(numberOf(execution), 'stdout', { byteLength: 0, captureOutcome: 'RecoveredAfterHostInterruption', truncated: null }),
          text: '',
          nextOffset: 0,
          totalLength: 0,
        }),
    })
    const view = mount()
    await openAndSelect(view)
    expect(view.result.current.selection?.descriptor ?? null).toBeNull()
    act(() => view.result.current.selectStream('stdout'))
    await flush()
    expect(view.result.current.selection?.descriptor).toMatchObject({ truncated: null, byteLength: 0 })
    act(() => view.result.current.window.load())
    await flush()
    expect(view.result.current.window.state).toMatchObject({ status: 'loaded', text: '', totalLength: 0, isComplete: true })

    for (const status of ['Unavailable', 'NotRecorded', 'Missing', 'IntegrityMismatch'] as const) {
      const refused = install({ window: (id, execution) => Promise.resolve(refusedWindowFor(numberOf(execution), 'stdout', status, 0, id)) })
      const again = mount()
      await openAndSelect(again)
      act(() => again.result.current.selectStream('stdout'))
      await flush()
      act(() => again.result.current.window.load())
      await flush()
      expect(refused.windows).toHaveLength(1)
      expect(again.result.current.window.state.status).toBe(
        { Unavailable: 'unavailable', NotRecorded: 'not-recorded', Missing: 'missing', IntegrityMismatch: 'integrity-mismatch' }[status],
      )
      again.unmount()
    }
  })

  it('treats an incoherent window as invalid without using any of it, and a failed window can be retried at the same offset', async () => {
    let attempt = 0
    const calls = install({
      window: (id, execution) => {
        attempt += 1
        return attempt === 1
          ? Promise.resolve({ ...windowFor(numberOf(execution), 'stdout', {}, id), totalLength: 3 })
          : attempt === 2
            ? Promise.reject(new Error('network'))
            : Promise.resolve(windowFor(numberOf(execution), 'stdout', {}, id))
      },
    })
    const view = mount()
    await openAndSelect(view)
    act(() => view.result.current.selectStream('stdout'))
    await flush()

    act(() => view.result.current.window.load())
    await flush()
    expect(view.result.current.window.state).toEqual({ status: 'error', failure: 'invalid', text: '' })
    act(() => view.result.current.window.load())
    await flush()
    expect(view.result.current.window.state).toEqual({ status: 'error', failure: 'read', text: '' })
    act(() => view.result.current.window.load())
    await flush()
    expect(view.result.current.window.state).toMatchObject({ status: 'loaded' })
    expect(calls.windows.map((call) => call.from)).toEqual([0, 0, 0])
  })

  it('reads a window under a ten-second bound: a hung request is aborted and read as a failed read', async () => {
    vi.useFakeTimers()
    const gate = deferred<unknown>()
    const calls = install({ window: () => gate.promise })
    const view = mount()
    await openAndSelect(view)
    act(() => view.result.current.selectStream('stdout'))
    await flush()
    act(() => view.result.current.window.load())
    await flush()
    expect(view.result.current.window.state.status).toBe('loading')
    expect(calls.windows[0].signal?.aborted).toBe(false)

    await act(async () => {
      vi.advanceTimersByTime(10_000)
    })

    expect(view.result.current.window.state).toEqual({ status: 'error', failure: 'read', text: '' })
    expect(calls.windows[0].signal?.aborted).toBe(true)
  })

  it('maps a 404 of the window to its own not-found state', async () => {
    install({ window: () => Promise.reject(notFound()) })
    const view = mount()
    await openAndSelect(view)
    act(() => view.result.current.selectStream('stdout'))
    await flush()
    act(() => view.result.current.window.load())
    await flush()
    expect(view.result.current.window.state).toEqual({ status: 'error', failure: 'not-found', text: '' })
  })

  it('closing then reopening is a new lifetime: nothing earlier is shown and a late answer of the earlier opening settles nothing', async () => {
    const first = deferred<unknown>()
    let reads = 0
    const calls = install({
      page: (id) => {
        reads += 1
        return reads === 1 ? first.promise : Promise.resolve(pageFor(10, 3, {}, id))
      },
    })
    const { result } = mount()
    act(() => result.current.show())
    await flush()
    expect(result.current.pending).toBe(true)

    act(() => result.current.close())
    expect(result.current.open).toBe(false)
    expect(result.current.page).toBeNull()
    expect(calls.pages[0].signal?.aborted).toBe(true)

    act(() => result.current.show())
    await flush()
    expect(result.current.page?.status === 'Recorded' ? result.current.page.items.length : -1).toBe(3)

    await act(async () => {
      first.resolve(pageFor(45, 20))
      await first.promise
    })
    expect(result.current.page?.status === 'Recorded' ? result.current.page.items.length : -1).toBe(3)
    expect(calls.pages).toHaveLength(2)
  })

  it('shows nothing of project A for project B, nor for a return to A (A-B-A), whatever late answers do', async () => {
    const lateA = deferred<unknown>()
    let reads = 0
    const calls = install({
      page: (id) => {
        reads += 1
        return reads === 1 ? lateA.promise : Promise.resolve(pageFor(id === PROJECT ? 9 : 8, 3, {}, id))
      },
    })
    const view = mount(PROJECT)
    act(() => view.result.current.show())
    await flush()

    view.renders.length = 0
    view.rerender({ id: PROJECT_B })
    for (const render of view.renders) {
      expect(render.open).toBe(false)
      expect(render.page).toBeNull()
      expect(render.pageKey).toBeNull()
    }

    act(() => view.result.current.show())
    await flush()
    expect(calls.pages.map((call) => call.projectId)).toEqual([PROJECT, PROJECT_B])
    expect(view.result.current.page?.status === 'Recorded' ? view.result.current.page.items[0].executionNumber : null).toBe(8)

    view.rerender({ id: PROJECT })
    expect(view.result.current.open).toBe(false)
    expect(view.result.current.page).toBeNull()

    await act(async () => {
      lateA.resolve(pageFor(45, 20, {}, PROJECT))
      await lateA.promise
    })
    expect(view.result.current.open).toBe(false)
    expect(view.result.current.page).toBeNull()
    expect(calls.pages).toHaveLength(2)

    act(() => view.result.current.show())
    await flush()
    expect(calls.pages.map((call) => call.projectId)).toEqual([PROJECT, PROJECT_B, PROJECT])
    expect(view.result.current.page?.status === 'Recorded' ? view.result.current.page.items[0].executionNumber : null).toBe(9)
  })

  it('lets a late answer of an unmounted hook settle nothing and makes every retained callback a no-op', async () => {
    const late = deferred<unknown>()
    const calls = install({ page: () => late.promise })
    const view = mount()
    act(() => view.result.current.show())
    await flush()
    const retained = view.result.current
    view.unmount()
    expect(calls.pages[0].signal?.aborted).toBe(true)

    await act(async () => {
      late.resolve(pageFor(45, 20))
      await late.promise
    })
    act(() => {
      retained.show()
      retained.older()
      retained.reload()
      retained.selectExecution(executionIdFor(45))
      retained.selectStream('stdout')
      retained.window.load()
    })
    await flush()

    expect(calls.pages).toHaveLength(1)
    expect(calls.executions).toEqual([])
    expect(calls.windows).toEqual([])
  })

  it('lets only the newest overlapping page visit settle, even when an older answer arrives last', async () => {
    const gates = [deferred<unknown>(), deferred<unknown>()]
    let reads = 0
    install({ page: () => gates[reads++].promise })
    const { result } = mount()
    act(() => result.current.show())
    await flush()
    const stale = result.current
    act(() => stale.reload())
    await flush()
    expect(reads).toBe(2)

    await act(async () => {
      gates[1].resolve(pageFor(7, 2))
      await gates[1].promise
    })
    expect(result.current.page?.status === 'Recorded' ? result.current.page.items.length : -1).toBe(2)
    await act(async () => {
      gates[0].resolve(pageFor(45, 20))
      await gates[0].promise
    })
    expect(result.current.page?.status === 'Recorded' ? result.current.page.items.length : -1).toBe(2)

    // A reload retained from the replaced visit decides against the queued value and replaces nothing again.
    act(() => stale.reload())
    await flush()
    expect(reads).toBe(2)
  })

  it('reads a page under a ten-second bound, aborts a hung request, and retries only the failed page read', async () => {
    vi.useFakeTimers()
    let reads = 0
    const calls = install({
      page: (id) => {
        reads += 1
        return reads === 1 ? new Promise(() => undefined) : Promise.resolve(pageFor(5, 2, {}, id))
      },
    })
    const { result } = mount()
    act(() => result.current.show())
    await flush()
    expect(result.current.pending).toBe(true)

    await act(async () => {
      vi.advanceTimersByTime(9_999)
    })
    expect(result.current.pending).toBe(true)
    await act(async () => {
      vi.advanceTimersByTime(1)
    })

    expect(result.current.failure).toBe('timeout')
    expect(result.current.page).toBeNull()
    expect(calls.pages[0].signal?.aborted).toBe(true)

    act(() => result.current.retryPage())
    await flush()
    expect(calls.pages.map((call) => call.before)).toEqual([null, null])
    expect(result.current.failure).toBeNull()
    expect(result.current.page?.status).toBe('Recorded')

    // Nothing to retry once it succeeded, and a retry never reads anything else.
    act(() => result.current.retryPage())
    await flush()
    expect(calls.pages).toHaveLength(2)
    expect(calls.executions).toEqual([])
  })

  it('retries a failed older page with the same cursor', async () => {
    let reads = 0
    const calls = install({
      page: (id, before) => {
        reads += 1
        return reads === 2 ? Promise.reject(new Error('network')) : Promise.resolve(pageFor(before === null ? 45 : before - 1, 20, {}, id))
      },
    })
    const { result } = mount()
    act(() => result.current.show())
    await flush()
    act(() => result.current.older())
    await flush()
    expect(result.current.failure).toBe('read')

    act(() => result.current.retryPage())
    await flush()

    expect(calls.pages.map((call) => call.before)).toEqual([null, 26, 26])
    expect(result.current.failure).toBeNull()
  })

  it('classifies a 404, a network failure and an incoherent answer, each clearing the page that was shown', async () => {
    let next: () => Promise<unknown> = () => Promise.reject(notFound())
    install({ page: () => next() })
    const { result } = mount()
    act(() => result.current.show())
    await flush()
    expect(result.current).toMatchObject({ failure: 'not-found', page: null, pending: false })

    install()
    act(() => result.current.reload())
    await flush()
    expect(result.current.page?.status).toBe('Recorded')

    next = () => Promise.reject(new Error('network'))
    install({ page: () => next() })
    act(() => result.current.reload())
    await flush()
    expect(result.current).toMatchObject({ failure: 'read', page: null })

    install({ page: () => Promise.resolve({ ...pageFor(45, 20), items: [] }) })
    act(() => result.current.reload())
    await flush()
    expect(result.current).toMatchObject({ failure: 'invalid', page: null })

    install({ page: (id) => Promise.resolve(pageFor(5, 2, {}, id)) })
    act(() => result.current.reload())
    await flush()
    expect(result.current.page?.status).toBe('Recorded')
    install({ page: () => Promise.resolve(pageFor(5, 2, {}, guidOf('p', 7))) })
    act(() => result.current.reload())
    await flush()
    expect(result.current).toMatchObject({ failure: 'invalid', page: null })
  })

  it('shows an Unavailable page as such, with no item and no way to page', async () => {
    const calls = install({ page: (id) => Promise.resolve(unavailablePageFor(id)) })
    const { result } = mount()
    act(() => result.current.show())
    await flush()

    expect(result.current.page).toEqual({ status: 'Unavailable' })
    act(() => result.current.older())
    await flush()
    expect(calls.pages).toHaveLength(1)
  })

  it('reads the execution again only after its own failure, clearing success, and refuses a changed immutable fact', async () => {
    let reads = 0
    const calls = install({
      execution: (id, execution) => {
        reads += 1
        const number = numberOf(execution)
        return reads === 1
          ? Promise.reject(new Error('network'))
          : reads === 2
            ? Promise.resolve(inspectionFor(number, {}, { commandName: 'Renamed' }, id))
            : Promise.resolve(inspectionFor(number, {}, {}, id))
      },
    })
    const view = mount()
    await openAndSelect(view)
    expect(view.result.current).toMatchObject({ executionFailure: 'read', inspection: null })

    act(() => view.result.current.retryExecution())
    await flush()
    expect(view.result.current).toMatchObject({ executionFailure: 'invalid', inspection: null })

    act(() => view.result.current.retryExecution())
    await flush()
    expect(view.result.current.executionFailure).toBeNull()
    expect(view.result.current.inspection?.status).toBe('Recorded')
    expect(calls.executions).toHaveLength(3)
    expect(calls.pages).toHaveLength(1)

    act(() => view.result.current.retryExecution())
    await flush()
    expect(calls.executions).toHaveLength(3)
  })

  it('shows an Unavailable execution and a 404 execution without using any fact, and offers no stream', async () => {
    install({ execution: (id, execution) => Promise.resolve(unavailableInspectionFor(numberOf(execution), id)) })
    const view = mount()
    await openAndSelect(view)
    expect(view.result.current.inspection).toEqual({ status: 'Unavailable' })
    act(() => view.result.current.selectStream('stdout'))
    await flush()
    expect(view.result.current.selection).toBeNull()

    install({ execution: () => Promise.reject(notFound()) })
    act(() => view.result.current.clearExecution())
    await flush()
    act(() => view.result.current.selectExecution(executionIdFor(44)))
    await flush()
    expect(view.result.current).toMatchObject({ executionFailure: 'not-found', inspection: null })
  })

  it('reads a ten-second-bounded execution once and aborts it with its visit', async () => {
    vi.useFakeTimers()
    const calls = install({ execution: () => new Promise(() => undefined) })
    const view = mount()
    await openAndSelect(view)
    expect(view.result.current.executionPending).toBe(true)

    await act(async () => {
      vi.advanceTimersByTime(10_000)
    })
    expect(view.result.current.executionFailure).toBe('timeout')
    expect(calls.executions[0].signal?.aborted).toBe(true)

    install({ execution: () => new Promise(() => undefined) })
    act(() => view.result.current.clearExecution())
    act(() => view.result.current.selectExecution(executionIdFor(44)))
    await flush()
    const pending = view.result
    act(() => pending.current.clearExecution())
    await flush()
    expect(view.result.current.selectedItem).toBeNull()
  })

  it('ends a retained Hide or close of an earlier visit without ending a later visit of the same stream or opening', async () => {
    const calls = install()
    const view = mount()
    act(() => view.result.current.show())
    await flush()
    const retainedClose = view.result.current.close
    act(() => view.result.current.selectExecution(executionIdFor(45)))
    await flush()
    const retainedClearExecution = view.result.current.clearExecution
    act(() => view.result.current.selectStream('stdout'))
    await flush()
    const retainedClearStream = view.result.current.clearStream

    // Hide and choose again in one batch: the later choice is a new visit, so the retained Hide cannot end it.
    act(() => {
      view.result.current.clearStream()
      view.result.current.selectStream('stdout')
    })
    await flush()
    expect(view.result.current.selection).toMatchObject({ stream: 'stdout', visit: 2 })
    act(() => retainedClearStream())
    await flush()
    expect(view.result.current.selection).toMatchObject({ stream: 'stdout', visit: 2 })

    act(() => {
      view.result.current.clearExecution()
      view.result.current.selectExecution(executionIdFor(45))
    })
    await flush()
    expect(view.result.current.selectedItem?.executionNumber).toBe(45)
    act(() => retainedClearExecution())
    await flush()
    expect(view.result.current.selectedItem?.executionNumber).toBe(45)

    act(() => view.result.current.close())
    act(() => view.result.current.show())
    await flush()
    expect(view.result.current.open).toBe(true)
    act(() => retainedClose())
    await flush()
    expect(view.result.current.open).toBe(true)
    expect(view.result.current.selectedItem).toBeNull()
    expect(calls.pages).toHaveLength(2)
  })

  it('never lets a window request of an earlier stream visit settle the later one', async () => {
    const gates = [deferred<unknown>(), deferred<unknown>()]
    let reads = 0
    const calls = install({ window: () => gates[reads++].promise })
    const view = mount()
    await openAndSelect(view)
    act(() => view.result.current.selectStream('stdout'))
    await flush()
    act(() => view.result.current.window.load())
    await flush()
    expect(view.result.current.window.state.status).toBe('loading')

    act(() => view.result.current.clearStream())
    act(() => view.result.current.selectStream('stdout'))
    await flush()
    expect(view.result.current.window.state).toEqual({ status: 'idle' })
    expect(calls.windows[0].signal?.aborted).toBe(true)
    act(() => view.result.current.window.load())
    await flush()

    await act(async () => {
      gates[1].resolve(windowFor(45, 'stdout'))
      await gates[1].promise
    })
    expect(view.result.current.window.state).toMatchObject({ status: 'loaded', text: 'hello output' })
    await act(async () => {
      gates[0].resolve({ ...windowFor(45, 'stdout'), text: 'stale!' })
      await gates[0].promise
    })
    expect(view.result.current.window.state).toMatchObject({ status: 'loaded', text: 'hello output' })
  })
})
