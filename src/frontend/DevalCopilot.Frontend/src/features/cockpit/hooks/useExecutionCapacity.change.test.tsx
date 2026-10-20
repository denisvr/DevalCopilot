import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { executionCapacityClient, executionCapacityLimitClient } from '../../../api/clients'
import { ApiException, ApiProblemDetails } from '../../../api/generated/api-client'
import { deriveExecutionCapacityStartGate, describeExecutionCapacityChange, EXECUTION_CAPACITY_CHANGE_FEEDBACK, EXECUTION_CAPACITY_GUIDANCE } from '../executionCapacity'
import { availableCapacityWire, capacityWireFor } from '../executionCapacityFixture'
import {
  EXECUTION_CAPACITY_REFRESH_INTERVAL_MS,
  EXECUTION_CAPACITY_REQUEST_TIMEOUT_MS,
  useExecutionCapacity,
} from './useExecutionCapacity'

// ADR-0059: the explicit limit change of the host-session capacity lifetime. From submission until a read that started after the actual
// write transport settled has settled, the old reading is masked for mutating-start purposes; an earlier read, a late answer or a stale
// callback restores nothing; at most one write is in flight, it is aborted after ten seconds and with its lifetime, and it is never retried.
vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  executionCapacityClient: vi.fn(),
  executionCapacityLimitClient: vi.fn(),
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

type Hook = ReturnType<typeof useExecutionCapacity>

/** A scripted host: every read answers from `reads` in order (the last answer repeats), every write from `writes`. */
function installHost(reads: Array<() => Promise<unknown>>, writes: Array<(limit: number) => Promise<unknown>> = []) {
  const read = vi.fn(() => (reads.length > 1 ? reads.shift()! : reads[0])())
  vi.mocked(executionCapacityClient).mockReturnValue({ readExecutionCapacity: read } as unknown as ReturnType<typeof executionCapacityClient>)
  const signals: AbortSignal[] = []
  const change = vi.fn((limit: number) => (writes.length > 1 ? writes.shift()! : writes[0])(limit))
  vi.mocked(executionCapacityLimitClient).mockImplementation((signal?: AbortSignal) => {
    if (signal !== undefined) {
      signals.push(signal)
    }

    return { changeExecutionCapacityLimit: change } as unknown as ReturnType<typeof executionCapacityLimitClient>
  })
  return { read, change, signals }
}

const mounted = (enabled = true) =>
  renderHook((props: { enabled: boolean }) => useExecutionCapacity(props.enabled), { initialProps: { enabled } })
const mountedWithHint = () =>
  renderHook((props: { hint: number }) => useExecutionCapacity(true, props.hint), { initialProps: { hint: 0 } })

const flush = () => act(async () => undefined)
const tick = (milliseconds: number) => act(async () => vi.advanceTimersByTimeAsync(milliseconds))
const gate = (result: { current: Hook }) => deriveExecutionCapacityStartGate(result.current)
const feedback = (result: { current: Hook }) => describeExecutionCapacityChange(result.current.change, result.current.reading)
const problem = (status: number, code: string) =>
  ApiProblemDetails.fromJS({ type: 'urn:devalente:problem:validation', title: 'fixed', status, errors: [{ code, detail: 'fixed' }] })

describe('useExecutionCapacity limit change', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('starts idle with no outcome and offers an apply operation', async () => {
    installHost([() => Promise.resolve(availableCapacityWire())])
    const { result } = mounted()
    await flush()

    expect(result.current.change).toEqual({ phase: 'idle', outcome: null, requested: null })
    expect(typeof result.current.applyLimit).toBe('function')
    expect(gate(result).allowsMutatingStart).toBe(true)
  })

  it('masks the old Available reading from submission and withholds with the change guidance while the write is in flight', async () => {
    const write = deferred<unknown>()
    const host = installHost([() => Promise.resolve(availableCapacityWire())], [() => write.promise])
    const { result } = mounted()
    await flush()

    act(() => result.current.applyLimit(1))

    expect(host.change).toHaveBeenCalledTimes(1)
    expect(host.change).toHaveBeenCalledWith(1)
    expect(result.current.change).toMatchObject({ phase: 'applying', requested: 1 })
    expect(result.current.reading).toBeNull()
    expect(gate(result)).toEqual({ allowsMutatingStart: false, guidance: EXECUTION_CAPACITY_GUIDANCE.changing })
  })

  it('requires a new read after the write transport settled and allows again only once that read validates', async () => {
    const write = deferred<unknown>()
    const confirm = deferred<unknown>()
    const host = installHost(
      [() => Promise.resolve(availableCapacityWire()), () => confirm.promise],
      [() => write.promise],
    )
    const { result } = mounted()
    await flush()
    expect(host.read).toHaveBeenCalledTimes(1)

    act(() => result.current.applyLimit(1))
    await act(async () => write.resolve(availableCapacityWire(0, 1)))

    expect(host.read).toHaveBeenCalledTimes(2)
    expect(result.current.change).toMatchObject({ phase: 'confirming', requested: 1 })
    expect(gate(result).allowsMutatingStart).toBe(false)
    expect(feedback(result)).toBe(EXECUTION_CAPACITY_CHANGE_FEEDBACK.confirming)

    await act(async () => confirm.resolve(availableCapacityWire(0, 1)))

    expect(result.current.change).toMatchObject({ phase: 'idle', outcome: 'applied', requested: 1 })
    expect(result.current.reading).toMatchObject({ limit: 1, status: 'Available' })
    expect(gate(result).allowsMutatingStart).toBe(true)
    expect(feedback(result)).toBe(EXECUTION_CAPACITY_CHANGE_FEEDBACK.appliedOne)
  })

  it('does not trust the write answer: a confirming read of a different limit is reported as such, not as success', async () => {
    const host = installHost(
      [() => Promise.resolve(availableCapacityWire()), () => Promise.resolve(availableCapacityWire(0, 2))],
      [() => Promise.resolve(availableCapacityWire(0, 1))],
    )
    const { result } = mounted()
    await flush()

    act(() => result.current.applyLimit(1))
    await flush()

    expect(host.read).toHaveBeenCalledTimes(2)
    expect(result.current.change).toMatchObject({ phase: 'idle', outcome: 'applied', requested: 1 })
    expect(feedback(result)).toBe(EXECUTION_CAPACITY_CHANGE_FEEDBACK.differs)
  })

  it('never lets a read that was already pending before the change restore the old Available, and waits for a later read', async () => {
    const preChange = deferred<unknown>()
    const write = deferred<unknown>()
    const confirm = deferred<unknown>()
    const host = installHost(
      [() => Promise.resolve(availableCapacityWire()), () => preChange.promise, () => confirm.promise],
      [() => write.promise],
    )
    const { result } = mounted()
    await flush()
    await tick(EXECUTION_CAPACITY_REFRESH_INTERVAL_MS)
    expect(host.read).toHaveBeenCalledTimes(2)

    act(() => result.current.applyLimit(1))
    await act(async () => preChange.resolve(availableCapacityWire(0, 2)))

    // The pre-change answer settled while the write was in flight: nothing is restored.
    expect(result.current.change.phase).toBe('applying')
    expect(result.current.reading).toBeNull()
    expect(gate(result).allowsMutatingStart).toBe(false)

    await act(async () => write.resolve(availableCapacityWire(0, 1)))

    expect(host.read).toHaveBeenCalledTimes(3)
    expect(result.current.change.phase).toBe('confirming')
    expect(gate(result).allowsMutatingStart).toBe(false)

    await act(async () => confirm.resolve(availableCapacityWire(0, 1)))

    expect(result.current.change.phase).toBe('idle')
    expect(result.current.reading).toMatchObject({ limit: 1 })
    expect(gate(result).allowsMutatingStart).toBe(true)
  })

  it('does not accept a read that began before the write settled as the confirmation, even when it settles afterwards', async () => {
    const preChange = deferred<unknown>()
    const write = deferred<unknown>()
    const confirm = deferred<unknown>()
    const host = installHost(
      [() => Promise.resolve(availableCapacityWire()), () => preChange.promise, () => confirm.promise],
      [() => write.promise],
    )
    const { result } = mounted()
    await flush()
    await tick(EXECUTION_CAPACITY_REFRESH_INTERVAL_MS)
    act(() => result.current.applyLimit(1))
    await act(async () => write.resolve(availableCapacityWire(0, 1)))
    // The pre-change read is still in flight when the write settles, so the confirming read is coalesced behind it.
    expect(host.read).toHaveBeenCalledTimes(2)

    await act(async () => preChange.resolve(availableCapacityWire(0, 2)))

    expect(result.current.change.phase).toBe('confirming')
    expect(result.current.reading).toBeNull()
    expect(gate(result).allowsMutatingStart).toBe(false)
    expect(host.read).toHaveBeenCalledTimes(3)

    await act(async () => confirm.resolve(availableCapacityWire(0, 1)))

    expect(result.current.change.phase).toBe('idle')
    expect(result.current.reading).toMatchObject({ limit: 1 })
  })

  it('aborts the actual write transport after ten seconds, shows unconfirmed feedback, reads again and never infers success or rollback', async () => {
    const write = deferred<unknown>()
    const host = installHost(
      [() => Promise.resolve(availableCapacityWire()), () => Promise.resolve(availableCapacityWire(0, 2))],
      [() => write.promise],
    )
    const { result } = mounted()
    await flush()
    act(() => result.current.applyLimit(1))
    expect(host.signals[0].aborted).toBe(false)

    await tick(EXECUTION_CAPACITY_REQUEST_TIMEOUT_MS)
    expect(host.signals[0].aborted).toBe(true)
    await act(async () => write.reject(new DOMException('The operation was aborted.', 'AbortError')))

    // The five-second poll that fell due during the wait was coalesced: exactly one read, started after the write settled, confirms.
    expect(host.read).toHaveBeenCalledTimes(2)
    expect(result.current.change).toMatchObject({ phase: 'idle', outcome: 'unconfirmed', requested: 1 })
    expect(feedback(result)).toBe(EXECUTION_CAPACITY_CHANGE_FEEDBACK.unconfirmed)
    // The read is the truth: it shows limit two, and nothing was retried.
    expect(result.current.reading).toMatchObject({ limit: 2 })
    await tick(60_000)
    expect(host.change).toHaveBeenCalledTimes(1)
  })

  it('settles a write that fails at the transport as unconfirmed, reads again and never retries the mutation', async () => {
    const host = installHost(
      [() => Promise.resolve(availableCapacityWire()), () => Promise.resolve(availableCapacityWire(0, 1))],
      [() => Promise.reject(new TypeError('network down'))],
    )
    const { result } = mounted()
    await flush()

    act(() => result.current.applyLimit(1))
    await flush()

    expect(result.current.change).toMatchObject({ phase: 'idle', outcome: 'unconfirmed', requested: 1 })
    // The change may have committed: the read shows it, and the feedback neither claims success nor rollback.
    expect(result.current.reading).toMatchObject({ limit: 1 })
    expect(feedback(result)).toBe(EXECUTION_CAPACITY_CHANGE_FEEDBACK.unconfirmed)
    await tick(60_000)
    expect(host.change).toHaveBeenCalledTimes(1)
  })

  it.each([
    ['the lower-limit refusal', () => Promise.reject(problem(409, 'execution_capacity.limit_below_recorded_owners')), 'belowOwners'],
    ['an unavailable refusal', () => Promise.reject(problem(409, 'execution_capacity.unavailable')), 'refused'],
    ['an invalid request refusal', () => Promise.reject(problem(400, 'execution_capacity.invalid_request')), 'refused'],
    ['a session refusal', () => Promise.reject(new ApiException('unauthorized', 401, '{}', {}, null)), 'refused'],
    ['a server failure that may follow a commit', () => Promise.reject(new ApiException('server', 500, '{}', {}, null)), 'unconfirmed'],
  ] as const)('classifies %s as %s, reads again, and never reports success', async (_, respond, outcome) => {
    const host = installHost([() => Promise.resolve(availableCapacityWire())], [respond])
    const { result } = mounted()
    await flush()

    act(() => result.current.applyLimit(1))
    await flush()

    expect(host.read).toHaveBeenCalledTimes(2)
    expect(result.current.change).toMatchObject({ phase: 'idle', outcome, requested: 1 })
    expect(feedback(result)).not.toBe(EXECUTION_CAPACITY_CHANGE_FEEDBACK.appliedOne)
    expect(feedback(result)).toBe(EXECUTION_CAPACITY_CHANGE_FEEDBACK[outcome])
  })

  it('allows only one write at a time: another apply while applying or confirming starts nothing', async () => {
    const write = deferred<unknown>()
    const confirm = deferred<unknown>()
    const host = installHost(
      [() => Promise.resolve(availableCapacityWire()), () => confirm.promise],
      [() => write.promise],
    )
    const { result } = mounted()
    await flush()

    act(() => result.current.applyLimit(1))
    act(() => result.current.applyLimit(2))
    act(() => result.current.applyLimit(1))
    expect(host.change).toHaveBeenCalledTimes(1)

    await act(async () => write.resolve(availableCapacityWire(0, 1)))
    act(() => result.current.applyLimit(2))
    expect(result.current.change.phase).toBe('confirming')
    expect(host.change).toHaveBeenCalledTimes(1)
  })

  it('keeps withholding when the confirming read fails or is incoherent, until a valid current read arrives', async () => {
    const host = installHost(
      [
        () => Promise.resolve(availableCapacityWire()),
        () => Promise.reject(new Error('boom')),
        () => Promise.resolve(availableCapacityWire(0, 1)),
      ],
      [() => Promise.resolve(availableCapacityWire(0, 1))],
    )
    const { result } = mounted()
    await flush()

    act(() => result.current.applyLimit(1))
    await flush()

    expect(result.current.change.phase).toBe('idle')
    expect(result.current.failure).toBe('read')
    expect(gate(result).allowsMutatingStart).toBe(false)
    expect(feedback(result)).toBe(EXECUTION_CAPACITY_CHANGE_FEEDBACK.unconfirmed)

    await tick(EXECUTION_CAPACITY_REFRESH_INTERVAL_MS)

    expect(host.read).toHaveBeenCalledTimes(3)
    expect(result.current.failure).toBeNull()
    expect(gate(result).allowsMutatingStart).toBe(true)
  })

  it('keeps polling after a confirmed change', async () => {
    const host = installHost([() => Promise.resolve(availableCapacityWire(0, 1))], [() => Promise.resolve(availableCapacityWire(0, 1))])
    const { result } = mounted()
    await flush()
    act(() => result.current.applyLimit(1))
    await flush()
    const reads = host.read.mock.calls.length

    await tick(EXECUTION_CAPACITY_REFRESH_INTERVAL_MS)

    expect(host.read.mock.calls.length).toBe(reads + 1)
  })

  it('aborts a write in flight when the host view unmounts and lets nothing settle afterwards', async () => {
    const write = deferred<unknown>()
    const host = installHost([() => Promise.resolve(availableCapacityWire())], [() => write.promise])
    const { result, unmount } = mounted()
    await flush()
    act(() => result.current.applyLimit(1))
    expect(host.signals[0].aborted).toBe(false)

    unmount()

    expect(host.signals[0].aborted).toBe(true)
    await act(async () => write.reject(new DOMException('aborted', 'AbortError')))
    await tick(EXECUTION_CAPACITY_REQUEST_TIMEOUT_MS)
    expect(host.change).toHaveBeenCalledTimes(1)
    expect(host.read).toHaveBeenCalledTimes(1)
  })

  it('aborts a write in flight when the session stops being ready, and a return starts a fresh idle lifetime', async () => {
    const write = deferred<unknown>()
    const host = installHost([() => Promise.resolve(availableCapacityWire())], [() => write.promise])
    const { result, rerender } = mounted()
    await flush()
    act(() => result.current.applyLimit(1))

    rerender({ enabled: false })

    expect(host.signals[0].aborted).toBe(true)
    expect(result.current.reading).toBeNull()
    await act(async () => write.resolve(availableCapacityWire(0, 1)))

    rerender({ enabled: true })
    await flush()

    expect(result.current.change).toEqual({ phase: 'idle', outcome: null, requested: null })
    expect(result.current.reading).toMatchObject({ limit: 2 })
    expect(host.change).toHaveBeenCalledTimes(1)
  })

  it('does nothing when an apply retained from an ended lifetime is called', async () => {
    const host = installHost([() => Promise.resolve(availableCapacityWire())], [() => Promise.resolve(availableCapacityWire(0, 1))])
    const { result, rerender } = mounted()
    await flush()
    const stale = result.current.applyLimit

    rerender({ enabled: false })
    rerender({ enabled: true })
    await flush()
    act(() => stale(1))

    expect(host.change).not.toHaveBeenCalled()
    expect(result.current.change.phase).toBe('idle')
  })

  it('does nothing before the session is ready', () => {
    const host = installHost([() => Promise.resolve(availableCapacityWire())], [() => Promise.resolve(availableCapacityWire(0, 1))])
    const { result } = mounted(false)

    act(() => result.current.applyLimit(1))

    expect(host.change).not.toHaveBeenCalled()
    expect(result.current.change.phase).toBe('idle')
  })

  it('refuses a value other than one or two without any request', async () => {
    const host = installHost([() => Promise.resolve(availableCapacityWire())], [() => Promise.resolve(availableCapacityWire(0, 1))])
    const { result } = mounted()
    await flush()

    act(() => result.current.applyLimit(3 as never))
    act(() => result.current.applyLimit(0 as never))

    expect(host.change).not.toHaveBeenCalled()
    expect(result.current.change.phase).toBe('idle')
  })

  describe('reads are coalesced around the write (GLL-R1)', () => {
    it('starts no read when the poll falls due while the write is unsettled, and rearms nothing', async () => {
      const write = deferred<unknown>()
      const host = installHost([() => Promise.resolve(availableCapacityWire())], [() => write.promise])
      const { result } = mounted()
      await flush()
      act(() => result.current.applyLimit(1))

      await tick(EXECUTION_CAPACITY_REFRESH_INTERVAL_MS * 3)

      expect(host.read).toHaveBeenCalledTimes(1)
      expect(result.current.change.phase).toBe('applying')
    })

    it('coalesces a poll, repeated Refresh and hints into exactly one confirming read after the write settles', async () => {
      const write = deferred<unknown>()
      const confirm = deferred<unknown>()
      const host = installHost([() => Promise.resolve(availableCapacityWire()), () => confirm.promise], [() => write.promise])
      const { result, rerender } = mountedWithHint()
      await flush()
      act(() => result.current.applyLimit(1))

      await tick(EXECUTION_CAPACITY_REFRESH_INTERVAL_MS)
      act(() => result.current.refresh())
      act(() => result.current.refresh())
      rerender({ hint: 1 })
      rerender({ hint: 2 })
      await flush()
      expect(host.read).toHaveBeenCalledTimes(1)

      await act(async () => write.resolve(availableCapacityWire(0, 1)))

      expect(host.read).toHaveBeenCalledTimes(2)
      await tick(EXECUTION_CAPACITY_REFRESH_INTERVAL_MS)
      expect(host.read).toHaveBeenCalledTimes(2)
      await act(async () => confirm.resolve(availableCapacityWire(0, 1)))
      expect(result.current.change.phase).toBe('idle')
      expect(result.current.reading).toMatchObject({ limit: 1 })
    })

    it('lets a read pending at submission settle without confirming, restoring or rearming, then starts exactly one fresh read', async () => {
      const preChange = deferred<unknown>()
      const write = deferred<unknown>()
      const confirm = deferred<unknown>()
      const host = installHost(
        [() => Promise.resolve(availableCapacityWire()), () => preChange.promise, () => confirm.promise],
        [() => write.promise],
      )
      const { result } = mounted()
      await flush()
      await tick(EXECUTION_CAPACITY_REFRESH_INTERVAL_MS)
      expect(host.read).toHaveBeenCalledTimes(2)
      act(() => result.current.applyLimit(1))
      act(() => result.current.refresh())

      await act(async () => preChange.resolve(availableCapacityWire(0, 2)))
      await tick(EXECUTION_CAPACITY_REFRESH_INTERVAL_MS * 2)

      expect(host.read).toHaveBeenCalledTimes(2)
      expect(result.current.reading).toBeNull()

      await act(async () => write.resolve(availableCapacityWire(0, 1)))

      expect(host.read).toHaveBeenCalledTimes(3)
      expect(result.current.change.phase).toBe('confirming')
      act(() => result.current.refresh())
      await act(async () => confirm.resolve(availableCapacityWire(0, 1)))
      expect(result.current.change.phase).toBe('idle')
      expect(result.current.reading).toMatchObject({ limit: 1 })
    })

    it('waits for an older read still in flight when the write settles and then starts exactly one fresh read', async () => {
      const preChange = deferred<unknown>()
      const write = deferred<unknown>()
      const host = installHost(
        [() => Promise.resolve(availableCapacityWire()), () => preChange.promise, () => Promise.resolve(availableCapacityWire(0, 1))],
        [() => write.promise],
      )
      const { result } = mounted()
      await flush()
      await tick(EXECUTION_CAPACITY_REFRESH_INTERVAL_MS)
      act(() => result.current.applyLimit(1))
      act(() => result.current.refresh())
      await act(async () => write.resolve(availableCapacityWire(0, 1)))
      expect(host.read).toHaveBeenCalledTimes(2)

      await act(async () => preChange.resolve(availableCapacityWire(0, 2)))

      expect(host.read).toHaveBeenCalledTimes(3)
      expect(result.current.change.phase).toBe('idle')
      await tick(1)
      expect(host.read).toHaveBeenCalledTimes(3)
    })

    it('starts no read around a write that times out until its transport settled, then exactly one', async () => {
      const write = deferred<unknown>()
      const host = installHost(
        [() => Promise.resolve(availableCapacityWire()), () => Promise.resolve(availableCapacityWire(0, 2))],
        [() => write.promise],
      )
      const { result } = mounted()
      await flush()
      act(() => result.current.applyLimit(1))

      await tick(EXECUTION_CAPACITY_REQUEST_TIMEOUT_MS)
      expect(host.signals[0].aborted).toBe(true)
      expect(host.read).toHaveBeenCalledTimes(1)
      await act(async () => write.reject(new DOMException('aborted', 'AbortError')))

      expect(host.read).toHaveBeenCalledTimes(2)
      expect(result.current.change).toMatchObject({ phase: 'idle', outcome: 'unconfirmed' })
    })

    it('drops coalesced triggers with the lifetime: nothing is read after unmount', async () => {
      const write = deferred<unknown>()
      const host = installHost([() => Promise.resolve(availableCapacityWire())], [() => write.promise])
      const { result, unmount } = mounted()
      await flush()
      act(() => result.current.applyLimit(1))
      act(() => result.current.refresh())
      await tick(EXECUTION_CAPACITY_REFRESH_INTERVAL_MS)

      unmount()
      await act(async () => write.resolve(availableCapacityWire(0, 1)))
      await tick(EXECUTION_CAPACITY_REFRESH_INTERVAL_MS * 2)

      expect(host.read).toHaveBeenCalledTimes(1)
    })

    it('keeps ordinary polling after the confirmed change', async () => {
      const host = installHost([() => Promise.resolve(availableCapacityWire(0, 1))], [() => Promise.resolve(availableCapacityWire(0, 1))])
      const { result } = mounted()
      await flush()
      act(() => result.current.applyLimit(1))
      await flush()
      const reads = host.read.mock.calls.length
      await tick(EXECUTION_CAPACITY_REFRESH_INTERVAL_MS)
      expect(host.read.mock.calls.length).toBe(reads + 1)
    })
  })

  it('reports the validated at-capacity reading at limit one as a withholding state with the limit-one guidance', async () => {
    installHost([() => Promise.resolve(capacityWireFor('AtCapacity', {}, 1, 1))])
    const { result } = mounted()
    await flush()

    expect(gate(result)).toEqual({ allowsMutatingStart: false, guidance: EXECUTION_CAPACITY_GUIDANCE.AtCapacityLimitOne })
  })
})
