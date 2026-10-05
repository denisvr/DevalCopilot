// @vitest-environment jsdom
import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { CodexAccountUsageWarningWindowResponse, GetCodexAccountUsageWarningResponse } from '../../../api/generated/api-client'
import { getCodexAccountUsageWarningClient } from '../../../api/clients'
import { useCheckCodexAccountUsageWarning } from './useCheckCodexAccountUsageWarning'

vi.mock('../../../api/clients', () => ({
  getCodexAccountUsageWarningClient: vi.fn(),
}))

const OBSERVED = new Date('2026-10-05T12:00:00.000Z')

function response(state: 'Below' | 'Reached', threshold = 80, used = state === 'Below' ? 10 : 90) {
  return new GetCodexAccountUsageWarningResponse({
    state,
    reason: state === 'Reached' ? 'ThresholdReached' : undefined,
    thresholdPercent: threshold,
    observedAtUtc: OBSERVED,
    windows: [
      new CodexAccountUsageWarningWindowResponse({
        bucketId: 'codex',
        window: 'Primary',
        usedPercent: used,
        reachedThreshold: used >= threshold,
      }),
    ],
    providerReportedLimitReached: false,
  })
}

function mockClient(impl: (...args: unknown[]) => Promise<unknown>) {
  const getCodexAccountUsageWarning = vi.fn(impl)
  vi.mocked(getCodexAccountUsageWarningClient).mockReturnValue({ getCodexAccountUsageWarning } as never)
  return getCodexAccountUsageWarning
}

function deferred() {
  const calls: { resolve: (value: unknown) => void; reject: (reason: unknown) => void }[] = []
  const client = mockClient(() => new Promise((resolve, reject) => calls.push({ resolve, reject })))
  return { calls, client }
}

const mount = (initial: { runId?: string; identity?: string; percent?: number | null } = {}) =>
  renderHook(
    ({ runId, identity, percent }) => useCheckCodexAccountUsageWarning(runId, identity, percent),
    { initialProps: { runId: 'run-1', identity: 'a', percent: 80 as number | null, ...initial } },
  )

beforeEach(() => {
  vi.mocked(getCodexAccountUsageWarningClient).mockReset()
})

describe('useCheckCodexAccountUsageWarning', () => {
  it('reads nothing on mount, rerender or identity change: only an explicit check reads the account', () => {
    const client = mockClient(async () => response('Below'))
    const { result, rerender } = mount()

    rerender({ runId: 'run-1', identity: 'a', percent: 80 })
    rerender({ runId: 'run-1', identity: 'b', percent: 70 })
    rerender({ runId: 'run-2', identity: 'c', percent: 70 })

    expect(client).not.toHaveBeenCalled()
    expect(result.current.phase).toEqual({ phase: 'idle' })
  })

  it('performs exactly one GET for the committed run and shows a dated Below result for the shown warning', async () => {
    const client = mockClient(async () => response('Below'))
    const { result } = mount()

    await act(async () => {
      await result.current.check()
    })

    expect(client).toHaveBeenCalledTimes(1)
    expect(client).toHaveBeenCalledWith('run-1')
    const phase = result.current.phase
    expect(phase.phase).toBe('observed')
    expect(phase.phase === 'observed' && phase.view.kind).toBe('below')
  })

  it('does nothing without a saved warning', async () => {
    const client = mockClient(async () => response('Below'))
    const { result } = mount({ percent: null })

    await act(async () => {
      await result.current.check()
    })

    expect(client).not.toHaveBeenCalled()
    expect(result.current.phase).toEqual({ phase: 'idle' })
  })

  it('is pending during the read, hides the previous classification, and ignores a duplicate activation', async () => {
    const { calls, client } = deferred()
    const { result } = mount()

    let first: Promise<void> = Promise.resolve()
    act(() => {
      first = result.current.check()
    })
    await act(async () => {
      await result.current.check()
    })
    expect(result.current.phase).toEqual({ phase: 'pending' })
    expect(client).toHaveBeenCalledTimes(1)

    await act(async () => {
      calls[0].resolve(response('Reached'))
      await first
    })
    expect(result.current.phase.phase).toBe('observed')

    act(() => {
      void result.current.check()
    })
    expect(result.current.phase).toEqual({ phase: 'pending' })
    await act(async () => {
      calls[1].resolve(response('Below'))
    })
  })

  it('a failed check clears any earlier classification and shows only the failed state', async () => {
    let fail = false
    mockClient(async () => {
      if (fail) {
        throw new Error('sensitive detail')
      }
      return response('Reached')
    })
    const { result } = mount()
    await act(async () => {
      await result.current.check()
    })
    expect(result.current.phase.phase).toBe('observed')

    fail = true
    await act(async () => {
      await result.current.check()
    })

    expect(result.current.phase).toEqual({ phase: 'failed' })
  })

  it('a response for a different saved warning is never a classification', async () => {
    mockClient(async () => response('Below', 50))
    const { result } = mount({ percent: 80 })

    await act(async () => {
      await result.current.check()
    })

    const phase = result.current.phase
    expect(phase.phase === 'observed' && phase.view.kind).toBe('unavailable')
  })
})

describe('useCheckCodexAccountUsageWarning ownership (run plus authoritative warning)', () => {
  it('does not carry pending state or an observation to a replacement warning of the same run', async () => {
    const { calls } = deferred()
    const { result, rerender } = mount()
    let old: Promise<void> = Promise.resolve()
    act(() => {
      old = result.current.check()
    })
    expect(result.current.phase).toEqual({ phase: 'pending' })

    rerender({ runId: 'run-1', identity: 'b', percent: 90 })
    expect(result.current.phase).toEqual({ phase: 'idle' })

    await act(async () => {
      calls[0].resolve(response('Reached'))
      await old
    })
    expect(result.current.phase).toEqual({ phase: 'idle' })
  })

  it('A -> B -> A is a new lifetime: neither the old pending state nor its late completion returns', async () => {
    const { calls } = deferred()
    const { result, rerender } = mount()
    let old: Promise<void> = Promise.resolve()
    act(() => {
      old = result.current.check()
    })
    rerender({ runId: 'run-1', identity: 'b', percent: 90 })
    rerender({ runId: 'run-1', identity: 'a', percent: 80 })
    expect(result.current.phase).toEqual({ phase: 'idle' })

    await act(async () => {
      calls[0].resolve(response('Reached'))
      await old
    })
    expect(result.current.phase).toEqual({ phase: 'idle' })
  })

  it('A -> B -> A does not resurrect an earlier observation of A', async () => {
    mockClient(async () => response('Below'))
    const { result, rerender } = mount()
    await act(async () => {
      await result.current.check()
    })
    expect(result.current.phase.phase).toBe('observed')

    rerender({ runId: 'run-1', identity: 'b', percent: 90 })
    rerender({ runId: 'run-1', identity: 'a', percent: 80 })

    expect(result.current.phase).toEqual({ phase: 'idle' })
  })

  it('overlapping completions: a late result of the replaced owner never writes or releases the newer owner', async () => {
    const { calls, client } = deferred()
    const { result, rerender } = mount()
    let old: Promise<void> = Promise.resolve()
    act(() => {
      old = result.current.check()
    })
    rerender({ runId: 'run-1', identity: 'b', percent: 90 })
    let replacement: Promise<void> = Promise.resolve()
    act(() => {
      replacement = result.current.check()
    })
    expect(client).toHaveBeenCalledTimes(2)

    await act(async () => {
      calls[0].resolve(response('Below', 80, 1))
      await old
    })
    expect(result.current.phase).toEqual({ phase: 'pending' })
    await act(async () => {
      await result.current.check()
    })
    expect(client).toHaveBeenCalledTimes(2)

    await act(async () => {
      calls[1].resolve(response('Reached', 90, 95))
      await replacement
    })
    const phase = result.current.phase
    expect(phase.phase === 'observed' && phase.view.kind).toBe('reached')
  })

  it('a changed run starts a new lifetime and ignores the previous run completion', async () => {
    const { calls } = deferred()
    const { result, rerender } = mount()
    let old: Promise<void> = Promise.resolve()
    act(() => {
      old = result.current.check()
    })
    rerender({ runId: 'run-2', identity: 'a', percent: 80 })

    await act(async () => {
      calls[0].resolve(response('Reached'))
      await old
    })
    expect(result.current.phase).toEqual({ phase: 'idle' })
  })

  it('a retained check handler from an ended owner is inert and never reads', async () => {
    const client = mockClient(async () => response('Below'))
    const { result, rerender } = mount()
    const retained = result.current
    rerender({ runId: 'run-1', identity: 'b', percent: 90 })

    await act(async () => {
      await retained.check()
    })

    expect(client).not.toHaveBeenCalled()
    expect(result.current.phase).toEqual({ phase: 'idle' })
  })

  it('a retained handler of an ended owner never supersedes the current in-flight check', async () => {
    const { calls, client } = deferred()
    const { result, rerender } = mount()
    const retained = result.current
    rerender({ runId: 'run-1', identity: 'b', percent: 90 })
    let current: Promise<void> = Promise.resolve()
    act(() => {
      current = result.current.check()
    })

    await act(async () => {
      await retained.check()
    })
    expect(client).toHaveBeenCalledTimes(1)
    expect(result.current.phase).toEqual({ phase: 'pending' })

    await act(async () => {
      calls[0].resolve(response('Reached', 90, 90))
      await current
    })
    expect(result.current.phase.phase).toBe('observed')
  })

  it('an unmounted owner ignores a late resolution and rejection without a state-update warning', async () => {
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => undefined)
    const { calls } = deferred()
    const { result, unmount } = mount()
    let first: Promise<void> = Promise.resolve()
    act(() => {
      first = result.current.check()
    })
    unmount()

    await act(async () => {
      calls[0].resolve(response('Below'))
      await first
    })
    expect(errorSpy).not.toHaveBeenCalled()
    errorSpy.mockRestore()
  })
})
