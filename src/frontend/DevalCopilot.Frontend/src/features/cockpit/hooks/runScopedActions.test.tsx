import { act, renderHook } from '@testing-library/react'
import { StrictMode, type ReactNode } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiException } from '../../../api/generated/api-client'
import * as clients from '../../../api/clients'
import { useAuthorizeReviewCorrection } from './useAuthorizeReviewCorrection'
import { useRequestChallengeResolution } from './useRequestChallengeResolution'
import { useRequestChallengeResolutionRepairAttempt } from './useRequestChallengeResolutionRepairAttempt'
import { useRequestClaudeCriticalReview } from './useRequestClaudeCriticalReview'
import { useRequestClaudeCriticalReviewRepairAttempt } from './useRequestClaudeCriticalReviewRepairAttempt'
import { useRequestCodeReview } from './useRequestCodeReview'
import { useRequestCodeReviewRepairAttempt } from './useRequestCodeReviewRepairAttempt'
import { useRequestCodexPlanningAttempt } from './useRequestCodexPlanningAttempt'
import { useRequestCodexPlanningRepairAttempt } from './useRequestCodexPlanningRepairAttempt'
import { useRequestImplementation } from './useRequestImplementation'
import { useRequestReviewCorrection } from './useRequestReviewCorrection'
import { useSetClaudeModelPreference } from './useSetClaudeModelPreference'
import { useSetClaudeMutationTurnLimit } from './useSetClaudeMutationTurnLimit'
import { useSetCodexAssignmentPreference } from './useSetCodexAssignmentPreference'
import { useSetTokenStopThreshold } from './useSetTokenStopThreshold'
import { useSetTokenWarningThreshold } from './useSetTokenWarningThreshold'

vi.mock('../../../api/clients')

/** The behavior every in-scope asynchronous cockpit control must share: work belongs to one run's
 * mounted interaction lifetime, and an obsolete completion never reaches the current UI. */
interface Adapter {
  busy: boolean
  error: string | null
  /** Submits for `runId` (ignored by controls that bind the hook's own run). */
  invoke: (runId: string) => Promise<boolean>
}

interface Case {
  name: string
  use: (runId: string, done: () => void) => Adapter
  /** Installs the typed API operation the control calls. */
  mock: (operation: ReturnType<typeof vi.fn>) => void
  /** False for controls that have no post-success refresh callback. */
  callback: boolean
  /** True when the control lets a caller pass a run id that may differ from the hook's run. */
  explicitRun: boolean
  /** The message a fixed 409 problem-details refusal produces. */
  refusal?: string
}

function install(client: unknown, method: string) {
  return (operation: ReturnType<typeof vi.fn>) => {
    vi.mocked(client as () => unknown).mockReturnValue({ [method]: operation })
  }
}

const ordinary = (
  name: string,
  hook: (runId: string, done: () => void) => { requesting: boolean; error: string | null; request: (...a: string[]) => Promise<boolean> },
  client: unknown,
  method: string,
  ids: string[],
): Case => ({
  name,
  use: (runId, done) => {
    const h = hook(runId, done)
    return { busy: h.requesting, error: h.error, invoke: (r) => h.request(r, ...ids) }
  },
  mock: install(client, method),
  callback: true,
  explicitRun: true,
})

const repair = (
  name: string,
  hook: (runId: string, done: () => void) => { requesting: boolean; error: string | null; request: (id: string) => Promise<boolean> },
  client: unknown,
  method: string,
): Case => ({
  name,
  use: (runId, done) => {
    const h = hook(runId, done)
    return { busy: h.requesting, error: h.error, invoke: () => h.request('source-1') }
  },
  mock: install(client, method),
  callback: true,
  explicitRun: false,
})

const cases: Case[] = [
  ordinary('Planner', (r, d) => {
    const h = useRequestCodexPlanningAttempt(r, d)
    return { ...h, request: (run: string) => h.request(run) }
  }, clients.requestCodexPlanningAttemptClient, 'requestCodexPlanningAttempt', []),
  ordinary('CriticalReviewer', useRequestClaudeCriticalReview, clients.requestClaudeCriticalReviewClient, 'requestClaudeCriticalReview', ['proposal-1']),
  ordinary('Resolver', useRequestChallengeResolution, clients.requestChallengeResolutionClient, 'requestChallengeResolution', ['review-1']),
  ordinary('Implementer', (r, d) => useRequestImplementation(r, 'plan-1', d), clients.requestImplementationClient, 'requestImplementation', ['plan-1']),
  ordinary('CodeReviewer', useRequestCodeReview, clients.requestCodeReviewClient, 'requestCodeReview', ['report-1']),
  ordinary('ReviewCorrection', (r, d) => useRequestReviewCorrection(r, 'review-1', d), clients.requestReviewCorrectionClient, 'requestReviewCorrection', ['review-1']),
  repair('Planner repair', useRequestCodexPlanningRepairAttempt, clients.requestCodexPlanningRepairAttemptClient, 'requestCodexPlanningRepairAttempt'),
  repair('CriticalReviewer repair', useRequestClaudeCriticalReviewRepairAttempt, clients.requestClaudeCriticalReviewRepairAttemptClient, 'requestClaudeCriticalReviewRepairAttempt'),
  repair('Resolver repair', useRequestChallengeResolutionRepairAttempt, clients.requestChallengeResolutionRepairAttemptClient, 'requestChallengeResolutionRepairAttempt'),
  repair('CodeReviewer repair', useRequestCodeReviewRepairAttempt, clients.requestCodeReviewRepairAttemptClient, 'requestCodeReviewRepairAttempt'),
  {
    name: 'plain authorization',
    use: (runId, done) => {
      const h = useAuthorizeReviewCorrection(runId, done)
      return { busy: h.authorizing, error: h.error, invoke: (r) => h.authorize(r, 'escalation-1') }
    },
    mock: install(clients.authorizeReviewCorrectionClient, 'authorizeReviewCorrection'),
    callback: true,
    explicitRun: true,
  },
  {
    name: 'guided authorization',
    use: (runId, done) => {
      const h = useAuthorizeReviewCorrection(runId, done)
      return { busy: h.authorizing, error: h.error, invoke: (r) => h.authorize(r, 'escalation-1', 'Keep it small.') }
    },
    mock: install(clients.authorizeReviewCorrectionWithGuidanceClient, 'authorizeReviewCorrectionWithGuidance'),
    callback: true,
    explicitRun: true,
  },
  {
    name: 'Codex model/effort',
    use: (runId, done) => {
      const h = useSetCodexAssignmentPreference(runId, done)
      return { busy: h.saving, error: h.error, invoke: (r) => h.save(r, 'gpt-5', 'high') }
    },
    mock: install(clients.setCodexAssignmentPreferenceClient, 'setCodexAssignmentPreference'),
    callback: true,
    explicitRun: true,
  },
  {
    name: 'Claude model/effort',
    use: (runId) => {
      const h = useSetClaudeModelPreference(runId)
      return { busy: h.saving, error: h.error, invoke: (r) => h.save(r, 'sonnet', 'high') }
    },
    mock: install(clients.setClaudeModelPreferenceClient, 'setClaudeModelPreference'),
    callback: false,
    explicitRun: true,
  },
  {
    name: 'token warning',
    use: (runId) => {
      const h = useSetTokenWarningThreshold(runId)
      return { busy: h.saving, error: h.error, invoke: (r) => h.save(r, 'Codex', 500) }
    },
    mock: install(clients.setTokenWarningThresholdClient, 'setTokenWarningThreshold'),
    callback: false,
    explicitRun: true,
  },
  {
    name: 'token stop',
    use: (runId) => {
      const h = useSetTokenStopThreshold(runId)
      return { busy: h.saving, error: h.error, invoke: (r) => h.save(r, 'ClaudeCode', 500) }
    },
    mock: install(clients.setTokenStopThresholdClient, 'setTokenStopThreshold'),
    callback: false,
    explicitRun: true,
  },
  {
    name: 'Claude turn limit',
    use: (runId) => {
      const h = useSetClaudeMutationTurnLimit(runId)
      return { busy: h.saving, error: h.error, invoke: () => h.save('5') }
    },
    mock: install(clients.setClaudeMutationTurnLimitClient, 'setClaudeMutationTurnLimit'),
    callback: false,
    explicitRun: false,
    refusal: 'The Claude turn limit request changed concurrently. Reload the run and retry.',
  },
]

function controllable() {
  let resolve!: (value?: unknown) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<unknown>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

const FIXED_DETAIL = 'A fixed safe refusal from the server.'
function refusal() {
  return new ApiException('Conflict', 409, JSON.stringify({ errors: [{ detail: FIXED_DETAIL }] }), {}, null)
}

function mount(c: Case, runId: string, done: () => void = vi.fn(), wrapper?: ({ children }: { children: ReactNode }) => ReactNode) {
  return renderHook(({ runId: current, done: callback }) => c.use(current, callback), {
    initialProps: { runId, done },
    ...(wrapper ? { wrapper: wrapper as never } : {}),
  })
}

beforeEach(() => {
  vi.clearAllMocks()
})

describe.each(cases)('$name run-scoped action', (c) => {
  it('reports a current success once and releases the control', async () => {
    const done = vi.fn()
    c.mock(vi.fn().mockResolvedValue({}))
    const { result } = mount(c, 'run-A', done)

    let outcome!: boolean
    await act(async () => {
      outcome = await result.current.invoke('run-A')
    })

    expect(outcome).toBe(true)
    expect(result.current.busy).toBe(false)
    expect(result.current.error).toBeNull()
    if (c.callback) expect(done).toHaveBeenCalledTimes(1)
  })

  it('shows only the fixed safe text of a refusal and never the raw exception of another failure', async () => {
    const done = vi.fn()
    c.mock(vi.fn().mockRejectedValueOnce(refusal()).mockRejectedValueOnce(new Error('ECONNRESET at 10.0.0.7:5432')))
    const { result } = mount(c, 'run-A', done)

    await act(async () => {
      expect(await result.current.invoke('run-A')).toBe(false)
    })
    expect(result.current.error).toBe(c.refusal ?? FIXED_DETAIL)

    await act(async () => {
      expect(await result.current.invoke('run-A')).toBe(false)
    })
    expect(result.current.error).not.toContain('10.0.0.7')
    expect(result.current.busy).toBe(false)
    expect(done).not.toHaveBeenCalled()
  })

  it('ignores a late success after a run switch without refreshing or reporting success', async () => {
    const done = vi.fn()
    const a = controllable()
    c.mock(vi.fn().mockReturnValueOnce(a.promise))
    const { result, rerender } = mount(c, 'run-A', done)

    let pending!: Promise<boolean>
    act(() => {
      pending = result.current.invoke('run-A')
    })
    rerender({ runId: 'run-B', done })

    await act(async () => {
      a.resolve({})
      expect(await pending).toBe(false)
    })
    expect(result.current.busy).toBe(false)
    expect(result.current.error).toBeNull()
    expect(done).not.toHaveBeenCalled()
  })

  it('ignores a late failure after a run switch', async () => {
    const a = controllable()
    c.mock(vi.fn().mockReturnValueOnce(a.promise))
    const { result, rerender } = mount(c, 'run-A')

    let pending!: Promise<boolean>
    act(() => {
      pending = result.current.invoke('run-A')
    })
    rerender({ runId: 'run-B', done: vi.fn() })

    await act(async () => {
      a.reject(refusal())
      expect(await pending).toBe(false)
    })
    expect(result.current.error).toBeNull()
    expect(result.current.busy).toBe(false)
  })

  it('keeps run B pending when run A completes after B started, and completes B on its own terms', async () => {
    const done = vi.fn()
    const a = controllable()
    const b = controllable()
    const operation = vi.fn().mockReturnValueOnce(a.promise).mockReturnValueOnce(b.promise)
    c.mock(operation)
    const { result, rerender } = mount(c, 'run-A', done)

    let pendingA!: Promise<boolean>
    act(() => {
      pendingA = result.current.invoke('run-A')
    })
    rerender({ runId: 'run-B', done })
    let pendingB!: Promise<boolean>
    act(() => {
      pendingB = result.current.invoke('run-B')
    })
    expect(result.current.busy).toBe(true)
    expect(operation).toHaveBeenCalledTimes(2)

    await act(async () => {
      a.resolve({})
      expect(await pendingA).toBe(false)
    })
    expect(result.current.busy).toBe(true)
    expect(done).not.toHaveBeenCalled()

    await act(async () => {
      b.resolve({})
      expect(await pendingB).toBe(true)
    })
    expect(result.current.busy).toBe(false)
    if (c.callback) expect(done).toHaveBeenCalledTimes(1)
  })

  it('never restores an old error or a stale failure after A to B to A', async () => {
    const late = controllable()
    c.mock(vi.fn().mockRejectedValueOnce(refusal()).mockReturnValueOnce(late.promise))
    const { result, rerender } = mount(c, 'run-A')

    await act(async () => {
      await result.current.invoke('run-A')
    })
    expect(result.current.error).not.toBeNull()

    rerender({ runId: 'run-B', done: vi.fn() })
    rerender({ runId: 'run-A', done: vi.fn() })
    expect(result.current.error).toBeNull()
    expect(result.current.busy).toBe(false)

    let pending!: Promise<boolean>
    act(() => {
      pending = result.current.invoke('run-A')
    })
    rerender({ runId: 'run-B', done: vi.fn() })
    rerender({ runId: 'run-A', done: vi.fn() })
    expect(result.current.busy).toBe(false)
    await act(async () => {
      late.reject(refusal())
      expect(await pending).toBe(false)
    })
    expect(result.current.error).toBeNull()
  })

  it('treats a remount as a new lifetime: the old completion is ignored and the new mount stays clean', async () => {
    const done = vi.fn()
    const old = controllable()
    c.mock(vi.fn().mockReturnValueOnce(old.promise))
    const first = mount(c, 'run-A', done)

    let pending!: Promise<boolean>
    act(() => {
      pending = first.result.current.invoke('run-A')
    })
    first.unmount()
    const second = mount(c, 'run-A', done)

    await act(async () => {
      old.resolve({})
      expect(await pending).toBe(false)
    })
    expect(second.result.current.busy).toBe(false)
    expect(second.result.current.error).toBeNull()
    expect(done).not.toHaveBeenCalled()
  })

  it('ignores a duplicate submission while one is in flight without a second API call', async () => {
    const done = vi.fn()
    const first = controllable()
    const operation = vi.fn().mockReturnValueOnce(first.promise)
    c.mock(operation)
    const { result } = mount(c, 'run-A', done)

    let pending!: Promise<boolean>
    act(() => {
      pending = result.current.invoke('run-A')
    })
    await act(async () => {
      expect(await result.current.invoke('run-A')).toBe(false)
    })
    expect(operation).toHaveBeenCalledTimes(1)
    expect(result.current.busy).toBe(true)

    await act(async () => {
      first.resolve({})
      expect(await pending).toBe(true)
    })
    expect(result.current.busy).toBe(false)
    if (c.callback) expect(done).toHaveBeenCalledTimes(1)
  })

  it('allows a new submission once the previous one settled', async () => {
    const operation = vi.fn().mockRejectedValueOnce(refusal()).mockResolvedValueOnce({})
    c.mock(operation)
    const { result } = mount(c, 'run-A')

    await act(async () => {
      expect(await result.current.invoke('run-A')).toBe(false)
    })
    await act(async () => {
      expect(await result.current.invoke('run-A')).toBe(true)
    })
    expect(operation).toHaveBeenCalledTimes(2)
    expect(result.current.error).toBeNull()
  })

  it('completes across a same-run rerender and across StrictMode double effects', async () => {
    const pendingOp = controllable()
    const done = vi.fn()
    c.mock(vi.fn().mockReturnValueOnce(pendingOp.promise))
    const { result, rerender } = mount(c, 'run-A', done, ({ children }) => <StrictMode>{children}</StrictMode>)

    let pending!: Promise<boolean>
    act(() => {
      pending = result.current.invoke('run-A')
    })
    rerender({ runId: 'run-A', done: vi.fn() })
    expect(result.current.busy).toBe(true)

    await act(async () => {
      pendingOp.resolve({})
      expect(await pending).toBe(true)
    })
    expect(result.current.busy).toBe(false)
    if (c.callback) expect(done).toHaveBeenCalledTimes(1)
  })

  if (c.explicitRun) {
    it('rejects a foreign run id before the API and keeps a valid current request pending', async () => {
      const done = vi.fn()
      const current = controllable()
      const operation = vi.fn().mockReturnValueOnce(current.promise)
      c.mock(operation)
      const { result } = mount(c, 'run-A', done)

      await act(async () => {
        expect(await result.current.invoke('run-OTHER')).toBe(false)
      })
      expect(operation).not.toHaveBeenCalled()
      expect(result.current.busy).toBe(false)

      let pending!: Promise<boolean>
      act(() => {
        pending = result.current.invoke('run-A')
      })
      await act(async () => {
        expect(await result.current.invoke('run-OTHER')).toBe(false)
      })
      expect(operation).toHaveBeenCalledTimes(1)
      expect(result.current.busy).toBe(true)

      await act(async () => {
        current.resolve({})
        expect(await pending).toBe(true)
      })
      expect(result.current.busy).toBe(false)
    })

    it('rejects a handler captured for run A after the switch to B without touching B', async () => {
      const stale = controllable()
      const b = controllable()
      const operation = vi.fn().mockReturnValueOnce(b.promise).mockReturnValueOnce(stale.promise)
      c.mock(operation)
      const { result, rerender } = mount(c, 'run-A')
      const staleInvoke = result.current.invoke

      rerender({ runId: 'run-B', done: vi.fn() })
      let pendingB!: Promise<boolean>
      act(() => {
        pendingB = result.current.invoke('run-B')
      })
      await act(async () => {
        expect(await staleInvoke('run-A')).toBe(false)
      })
      expect(operation).toHaveBeenCalledTimes(1)
      expect(result.current.busy).toBe(true)

      await act(async () => {
        b.resolve({})
        expect(await pendingB).toBe(true)
      })
    })
  }
})

describe('independent controls', () => {
  it('lets two controls of one run be pending at once and settle independently', async () => {
    const planning = controllable()
    const token = controllable()
    vi.mocked(clients.requestCodexPlanningAttemptClient).mockReturnValue({
      requestCodexPlanningAttempt: vi.fn().mockReturnValue(planning.promise),
    } as never)
    vi.mocked(clients.setTokenStopThresholdClient).mockReturnValue({
      setTokenStopThreshold: vi.fn().mockReturnValue(token.promise),
    } as never)
    const done = vi.fn()
    const { result } = renderHook(() => ({
      planning: useRequestCodexPlanningAttempt('run-A', done),
      stop: useSetTokenStopThreshold('run-A'),
    }))

    let p1!: Promise<boolean>
    let p2!: Promise<boolean>
    act(() => {
      p1 = result.current.planning.request('run-A')
      p2 = result.current.stop.save('run-A', 'Codex', 5)
    })
    expect(result.current.planning.requesting).toBe(true)
    expect(result.current.stop.saving).toBe(true)

    await act(async () => {
      token.resolve({})
      expect(await p2).toBe(true)
    })
    expect(result.current.planning.requesting).toBe(true)
    expect(result.current.stop.saving).toBe(false)

    await act(async () => {
      planning.resolve({})
      expect(await p1).toBe(true)
    })
    expect(done).toHaveBeenCalledTimes(1)
  })
})
