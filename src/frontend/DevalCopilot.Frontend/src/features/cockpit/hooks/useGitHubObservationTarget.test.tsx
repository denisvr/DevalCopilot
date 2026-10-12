import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { gitHubObservationTargetClient, recordGitHubObservationTargetClient } from '../../../api/clients'
import type { GetGitHubObservationTargetResponse, RecordGitHubObservationTargetResponse } from '../../../api/clients'
import { ApiException } from '../../../api/generated/api-client'
import type { ObservationSource } from '../gitHubObservationTarget'
import { useGitHubObservationTarget } from './useGitHubObservationTarget'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  gitHubObservationTargetClient: vi.fn(),
  recordGitHubObservationTargetClient: vi.fn(),
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

const SHA = 'a'.repeat(40)
const AT = new Date('2026-10-12T20:00:00.000Z')
const sourceOf = (runId: string, operationId = 'op-' + runId, branchName = 'devalcopilot/workspace/x/1'): ObservationSource => ({
  runId,
  operationId,
  commitSha: SHA,
  branchName,
})
const A = sourceOf('run-a')
const B = sourceOf('run-b')

const wireSource = (source: ObservationSource) => ({ operationId: source.operationId, commitSha: source.commitSha, branchName: source.branchName })
const wireTarget = (owner = 'octo-cat', repository = 'hello.world') => ({
  id: 'target-1',
  eventId: 'event-1',
  host: 'github.com',
  owner,
  repository,
  recordedAtUtc: AT,
})
const notRecorded = (source: ObservationSource, withSource = true) =>
  ({ runId: source.runId, state: 'NotRecorded', source: withSource ? wireSource(source) : undefined }) as unknown as GetGitHubObservationTargetResponse
const available = (source: ObservationSource, owner?: string, repository?: string) =>
  ({
    runId: source.runId,
    state: 'Available',
    source: wireSource(source),
    target: wireTarget(owner, repository),
  }) as unknown as GetGitHubObservationTargetResponse
const wrote = (source: ObservationSource, owner = 'octo-cat', repository = 'hello.world') =>
  ({ runId: source.runId, source: wireSource(source), target: wireTarget(owner, repository) }) as unknown as RecordGitHubObservationTargetResponse

function install(
  read: (runId: string) => Promise<GetGitHubObservationTargetResponse>,
  write: (runId: string, request: unknown) => Promise<RecordGitHubObservationTargetResponse> = (runId) => Promise.resolve(wrote(sourceOf(runId))),
) {
  const getGitHubObservationTarget = vi.fn(read)
  const recordGitHubObservationTarget = vi.fn(write)
  vi.mocked(gitHubObservationTargetClient).mockReturnValue(
    { getGitHubObservationTarget } as unknown as ReturnType<typeof gitHubObservationTargetClient>,
  )
  vi.mocked(recordGitHubObservationTargetClient).mockReturnValue(
    { recordGitHubObservationTarget } as unknown as ReturnType<typeof recordGitHubObservationTargetClient>,
  )
  return { get: getGitHubObservationTarget, record: recordGitHubObservationTarget }
}

const mountFor = (initial: ObservationSource | null) =>
  renderHook(({ source }: { source: ObservationSource | null }) => useGitHubObservationTarget(source), { initialProps: { source: initial } })

type Mounted = ReturnType<typeof mountFor>

/** Opens, loads a recordable NotRecorded reading and fills a valid draft. */
async function readyToRecord(mounted: Mounted) {
  act(() => mounted.result.current.show())
  await act(async () => mounted.result.current.reload())
  act(() => mounted.result.current.setOwner('octo-cat'))
  act(() => mounted.result.current.setRepository('hello.world'))
}

describe('useGitHubObservationTarget', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('reads and writes nothing on mount, opening, rerender, a source change or a timer', () => {
    vi.useFakeTimers()
    const { get, record } = install((id) => Promise.resolve(notRecorded(sourceOf(id))))
    const mounted = mountFor(A)

    act(() => mounted.result.current.show())
    mounted.rerender({ source: A })
    mounted.rerender({ source: B })
    mounted.rerender({ source: A })
    act(() => {
      vi.advanceTimersByTime(10 * 60 * 1000)
    })

    expect(get).not.toHaveBeenCalled()
    expect(record).not.toHaveBeenCalled()
  })

  it('is collapsed, idle and blank for a newly committed source, and does nothing without a source', () => {
    const { get, record } = install((id) => Promise.resolve(notRecorded(sourceOf(id))))
    const mounted = mountFor(null)

    act(() => mounted.result.current.show())
    act(() => mounted.result.current.reload())
    act(() => mounted.result.current.record())

    expect(mounted.result.current).toMatchObject({ open: false, load: { kind: 'idle' }, write: { kind: 'idle' }, owner: '', repository: '', canRecord: false })
    expect(get).not.toHaveBeenCalled()
    expect(record).not.toHaveBeenCalled()
  })

  it('does nothing when asked to read or record while collapsed', () => {
    const { get, record } = install((id) => Promise.resolve(notRecorded(sourceOf(id))))
    const mounted = mountFor(A)

    act(() => mounted.result.current.reload())
    act(() => mounted.result.current.record())

    expect(get).not.toHaveBeenCalled()
    expect(record).not.toHaveBeenCalled()
  })

  it('reads the exact run once per explicit reload, hides any answer while pending and ignores a second activation in flight', async () => {
    const first = deferred<GetGitHubObservationTargetResponse>()
    const { get } = install(() => first.promise)
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())

    act(() => mounted.result.current.reload())
    act(() => mounted.result.current.reload())

    expect(get).toHaveBeenCalledTimes(1)
    expect(get).toHaveBeenCalledWith('run-a')
    expect(mounted.result.current.load).toEqual({ kind: 'pending' })

    await act(async () => first.resolve(notRecorded(A)))

    expect(mounted.result.current.load).toEqual({ kind: 'result', reading: { kind: 'NotRecorded', recordable: true } })
  })

  it('sends one request for two activations in the same batch', async () => {
    const { get } = install((id) => Promise.resolve(notRecorded(sourceOf(id))))
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())

    await act(async () => {
      mounted.result.current.reload()
      mounted.result.current.reload()
    })

    expect(get).toHaveBeenCalledTimes(1)
  })

  it('shows a read failure and an incoherent answer as distinct failures, with no partial reading', async () => {
    const { get } = install(() => Promise.reject(new Error('offline')))
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())

    await act(async () => mounted.result.current.reload())
    expect(mounted.result.current.load).toEqual({ kind: 'failed', reason: 'read' })

    get.mockResolvedValueOnce({ runId: 'run-a', state: 'Available', source: wireSource(A) } as unknown as GetGitHubObservationTargetResponse)
    await act(async () => mounted.result.current.reload())
    expect(mounted.result.current.load).toEqual({ kind: 'failed', reason: 'invalid' })

    get.mockResolvedValueOnce(notRecorded(B))
    await act(async () => mounted.result.current.reload())
    expect(mounted.result.current.load).toEqual({ kind: 'failed', reason: 'invalid' })
  })

  it('cannot record before a coherent recordable reading, without a valid draft, or when the host named no source', async () => {
    const { record } = install((id) => Promise.resolve(notRecorded(sourceOf(id), false)))
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())

    act(() => mounted.result.current.setOwner('octo-cat'))
    act(() => mounted.result.current.setRepository('hello.world'))
    act(() => mounted.result.current.record())
    expect(mounted.result.current.canRecord).toBe(false)

    await act(async () => mounted.result.current.reload())
    expect(mounted.result.current.load).toEqual({ kind: 'result', reading: { kind: 'NotRecorded', recordable: false } })
    expect(mounted.result.current.canRecord).toBe(false)
    act(() => mounted.result.current.record())

    expect(record).not.toHaveBeenCalled()
  })

  it('needs both names, exactly as typed, before recording is possible', async () => {
    const { record } = install((id) => Promise.resolve(notRecorded(sourceOf(id))))
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())
    await act(async () => mounted.result.current.reload())

    expect(mounted.result.current).toMatchObject({ owner: '', repository: '', canRecord: false })
    for (const [owner, repository] of [
      ['octo-cat', ''],
      ['', 'hello'],
      [' octo-cat', 'hello'],
      ['octo-cat', 'hello '],
      ['-octo', 'hello'],
      ['octo', '.hello'],
    ]) {
      act(() => mounted.result.current.setOwner(owner))
      act(() => mounted.result.current.setRepository(repository))
      expect(mounted.result.current.canRecord).toBe(false)
      act(() => mounted.result.current.record())
    }

    expect(record).not.toHaveBeenCalled()
    act(() => mounted.result.current.setOwner('octo-cat'))
    act(() => mounted.result.current.setRepository('hello'))
    expect(mounted.result.current.canRecord).toBe(true)
  })

  it('records exactly the observed source and typed names once, and accepts only the whole matching answer', async () => {
    const write = deferred<RecordGitHubObservationTargetResponse>()
    const { record } = install((id) => Promise.resolve(notRecorded(sourceOf(id))), () => write.promise)
    const mounted = mountFor(A)
    await readyToRecord(mounted)

    act(() => mounted.result.current.record())
    act(() => mounted.result.current.record())

    expect(record).toHaveBeenCalledTimes(1)
    expect(record.mock.calls[0][0]).toBe('run-a')
    expect(record.mock.calls[0][1]).toMatchObject({
      operationId: 'op-run-a',
      commitSha: SHA,
      owner: 'octo-cat',
      repository: 'hello.world',
    })
    expect(Object.keys(record.mock.calls[0][1] as object).sort()).toEqual(['commitSha', 'operationId', 'owner', 'repository'])
    expect(mounted.result.current.write).toEqual({ kind: 'pending' })
    expect(mounted.result.current.canRecord).toBe(false)

    await act(async () => write.resolve(wrote(A)))

    expect(mounted.result.current.write).toEqual({ kind: 'idle' })
    expect(mounted.result.current.load).toMatchObject({
      kind: 'result',
      reading: { kind: 'Available', target: { owner: 'octo-cat', repository: 'hello.world', host: 'github.com', recordedAtUtc: AT } },
    })
    act(() => mounted.result.current.record())
    expect(record).toHaveBeenCalledTimes(1)
  })

  it('sends one request for two record activations in the same batch', async () => {
    const { record } = install((id) => Promise.resolve(notRecorded(sourceOf(id))))
    const mounted = mountFor(A)
    await readyToRecord(mounted)

    await act(async () => {
      mounted.result.current.record()
      mounted.result.current.record()
    })

    expect(record).toHaveBeenCalledTimes(1)
  })

  it('ignores draft edits while recording is in flight', async () => {
    const write = deferred<RecordGitHubObservationTargetResponse>()
    install((id) => Promise.resolve(notRecorded(sourceOf(id))), () => write.promise)
    const mounted = mountFor(A)
    await readyToRecord(mounted)
    act(() => mounted.result.current.record())

    act(() => mounted.result.current.setOwner('someone-else'))
    act(() => mounted.result.current.setRepository('other'))

    expect(mounted.result.current).toMatchObject({ owner: 'octo-cat', repository: 'hello.world' })
    await act(async () => write.resolve(wrote(A)))
  })

  it.each([
    ['an answer for other names', () => Promise.resolve(wrote(A, 'other', 'hello.world'))],
    ['an answer for another source', () => Promise.resolve(wrote(B))],
    ['an incoherent answer', () => Promise.resolve({ runId: 'run-a' } as unknown as RecordGitHubObservationTargetResponse)],
    ['a network failure', () => Promise.reject(new Error('offline'))],
    ['a server failure', () => Promise.reject(new ApiException('boom', 500, '', {}, null))],
  ])('treats %s as an unknown outcome and refuses to record again until a reload has read the recorded state', async (_name, write) => {
    const { get, record } = install((id) => Promise.resolve(notRecorded(sourceOf(id))), write)
    const mounted = mountFor(A)
    await readyToRecord(mounted)
    const retained = mounted.result.current.record

    await act(async () => mounted.result.current.record())

    expect(mounted.result.current.write).toEqual({ kind: 'failed', reason: 'ambiguous' })
    expect(mounted.result.current.canRecord).toBe(false)
    act(() => retained())
    act(() => mounted.result.current.record())
    expect(record).toHaveBeenCalledTimes(1)

    // The explicit reload finds the original record and is the only way out.
    get.mockResolvedValueOnce(available(A))
    await act(async () => mounted.result.current.reload())
    expect(mounted.result.current.write).toEqual({ kind: 'idle' })
    expect(mounted.result.current.load).toMatchObject({ kind: 'result', reading: { kind: 'Available' } })
    act(() => retained())
    expect(record).toHaveBeenCalledTimes(1)
  })

  it('treats a definite refusal as a refusal, and a reload that finds nothing recorded allows recording again', async () => {
    const { record } = install(
      (id) => Promise.resolve(notRecorded(sourceOf(id))),
      () => Promise.reject(new ApiException('conflict', 409, '', {}, null)),
    )
    const mounted = mountFor(A)
    await readyToRecord(mounted)

    await act(async () => mounted.result.current.record())
    expect(mounted.result.current.write).toEqual({ kind: 'failed', reason: 'refused' })
    expect(mounted.result.current.canRecord).toBe(false)

    await act(async () => mounted.result.current.reload())
    expect(mounted.result.current.write).toEqual({ kind: 'idle' })
    expect(mounted.result.current.canRecord).toBe(true)
    await act(async () => mounted.result.current.record())
    expect(record).toHaveBeenCalledTimes(2)
  })

  it('keeps an unknown outcome blocked when the reload itself fails, and does not reload while recording', async () => {
    const write = deferred<RecordGitHubObservationTargetResponse>()
    const { get, record } = install((id) => Promise.resolve(notRecorded(sourceOf(id))), () => write.promise)
    const mounted = mountFor(A)
    await readyToRecord(mounted)
    act(() => mounted.result.current.record())

    act(() => mounted.result.current.reload())
    expect(get).toHaveBeenCalledTimes(1)

    await act(async () => write.reject(new Error('offline')))
    get.mockRejectedValueOnce(new Error('offline'))
    await act(async () => mounted.result.current.reload())

    expect(mounted.result.current.write).toEqual({ kind: 'failed', reason: 'ambiguous' })
    expect(mounted.result.current.canRecord).toBe(false)
    act(() => mounted.result.current.record())
    expect(record).toHaveBeenCalledTimes(1)
  })

  it('discards the draft, the reading and everything in flight when closed, and a reopening starts blank and unloaded', async () => {
    const late = deferred<RecordGitHubObservationTargetResponse>()
    const { get, record } = install((id) => Promise.resolve(notRecorded(sourceOf(id))), () => late.promise)
    const mounted = mountFor(A)
    await readyToRecord(mounted)
    act(() => mounted.result.current.record())
    const retained = mounted.result.current

    act(() => mounted.result.current.close())
    expect(mounted.result.current).toMatchObject({ open: false, load: { kind: 'idle' }, write: { kind: 'idle' }, owner: '' })
    await act(async () => late.resolve(wrote(A)))
    expect(mounted.result.current.load).toEqual({ kind: 'idle' })

    act(() => mounted.result.current.show())
    expect(mounted.result.current).toMatchObject({ open: true, load: { kind: 'idle' }, write: { kind: 'idle' }, owner: '', repository: '', canRecord: false })
    act(() => retained.reload())
    act(() => retained.record())
    act(() => retained.setOwner('octo-cat'))
    expect(get).toHaveBeenCalledTimes(1)
    expect(record).toHaveBeenCalledTimes(1)
    expect(mounted.result.current.owner).toBe('')
  })

  it('refuses a read, a write and a draft edit issued after a close in the same batch', async () => {
    const { get, record } = install((id) => Promise.resolve(notRecorded(sourceOf(id))))
    const mounted = mountFor(A)
    await readyToRecord(mounted)

    await act(async () => {
      mounted.result.current.close()
      mounted.result.current.record()
      mounted.result.current.reload()
      mounted.result.current.setOwner('other')
    })

    expect(record).not.toHaveBeenCalled()
    expect(get).toHaveBeenCalledTimes(1)
    expect(mounted.result.current.open).toBe(false)
  })

  it('shows nothing of a recording that was sent and then closed in the same batch, and a reopening cannot be affected by it', async () => {
    const late = deferred<RecordGitHubObservationTargetResponse>()
    const { record } = install((id) => Promise.resolve(notRecorded(sourceOf(id))), () => late.promise)
    const mounted = mountFor(A)
    await readyToRecord(mounted)

    await act(async () => {
      mounted.result.current.record()
      mounted.result.current.close()
    })
    expect(record).toHaveBeenCalledTimes(1)
    act(() => mounted.result.current.show())
    await act(async () => late.resolve(wrote(A)))

    expect(mounted.result.current).toMatchObject({ open: true, load: { kind: 'idle' }, write: { kind: 'idle' } })
  })

  it('treats another source, and a return to an earlier one, as a new collapsed lifetime that late answers cannot reach', async () => {
    const first = deferred<GetGitHubObservationTargetResponse>()
    const { get } = install((id) => (id === 'run-a' ? first.promise : Promise.resolve(notRecorded(sourceOf(id)))))
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())
    act(() => mounted.result.current.reload())

    mounted.rerender({ source: B })
    expect(mounted.result.current).toMatchObject({ open: false, load: { kind: 'idle' } })
    mounted.rerender({ source: A })
    expect(mounted.result.current).toMatchObject({ open: false, load: { kind: 'idle' }, owner: '' })

    await act(async () => first.resolve(notRecorded(A)))
    expect(mounted.result.current.load).toEqual({ kind: 'idle' })
    act(() => mounted.result.current.show())
    expect(mounted.result.current.load).toEqual({ kind: 'idle' })
    expect(get).toHaveBeenCalledTimes(1)
  })

  it('does not let a changed branch, operation or commit keep an earlier source lifetime', async () => {
    install((id) => Promise.resolve(notRecorded(sourceOf(id))))
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())
    await act(async () => mounted.result.current.reload())
    expect(mounted.result.current.open).toBe(true)

    for (const next of [{ ...A, branchName: 'other' }, { ...A, operationId: 'other' }, { ...A, commitSha: 'b'.repeat(40) }]) {
      mounted.rerender({ source: next })
      expect(mounted.result.current).toMatchObject({ open: false, load: { kind: 'idle' } })
      mounted.rerender({ source: A })
    }
  })

  it('ignores a retained callback of an unmounted lifetime and a late answer after unmount', async () => {
    const late = deferred<GetGitHubObservationTargetResponse>()
    const { get } = install(() => late.promise)
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())
    act(() => mounted.result.current.reload())
    const retained = mounted.result.current

    mounted.unmount()
    await act(async () => late.resolve(notRecorded(A)))
    act(() => retained.reload())
    act(() => retained.show())

    expect(get).toHaveBeenCalledTimes(1)
  })

  it.each(['owner', 'repository', 'A-B-A on the owner', 'A-B-A on the repository', 'an edit and a record in the same batch'])(
    'refuses a record callback retained from an ended draft: %s',
    async (change) => {
      const { record } = install((id) => Promise.resolve(notRecorded(sourceOf(id))))
      const mounted = mountFor(A)
      await readyToRecord(mounted)
      expect(mounted.result.current.canRecord).toBe(true)
      const retained = mounted.result.current.record

      if (change === 'an edit and a record in the same batch') {
        act(() => {
          mounted.result.current.setOwner('new-owner')
          retained()
        })
      } else {
        act(() => (change === 'repository' ? mounted.result.current.setRepository('new-repo') : mounted.result.current.setOwner('new-owner')))
        if (change === 'A-B-A on the owner') {
          act(() => mounted.result.current.setOwner('octo-cat'))
        }
        if (change === 'A-B-A on the repository') {
          act(() => mounted.result.current.setRepository('hello.world'))
        }
        act(() => retained())
      }

      expect(record).not.toHaveBeenCalled()
    },
  )

  it('submits the current edited draft exactly once, never the draft an earlier callback was created for', async () => {
    const { record } = install((id) => Promise.resolve(notRecorded(sourceOf(id))))
    const mounted = mountFor(A)
    await readyToRecord(mounted)
    const retained = mounted.result.current.record

    act(() => mounted.result.current.setOwner('new-owner'))
    act(() => mounted.result.current.setRepository('new-repo'))
    expect(mounted.result.current.canRecord).toBe(true)
    act(() => retained())
    expect(record).not.toHaveBeenCalled()

    await act(async () => {
      mounted.result.current.record()
      mounted.result.current.record()
    })

    expect(record).toHaveBeenCalledTimes(1)
    expect(record.mock.calls[0][1]).toMatchObject({ owner: 'new-owner', repository: 'new-repo' })
  })

  it('keeps recording armed for the current draft when an edit follows a reload, and an unchanged draft still submits once', async () => {
    const { record } = install((id) => Promise.resolve(notRecorded(sourceOf(id))))
    const mounted = mountFor(A)
    await readyToRecord(mounted)

    await act(async () => mounted.result.current.reload())
    act(() => mounted.result.current.setOwner('edited-owner'))
    await act(async () => mounted.result.current.record())

    expect(record).toHaveBeenCalledTimes(1)
    expect(record.mock.calls[0][1]).toMatchObject({ owner: 'edited-owner', repository: 'hello.world' })
  })

  it('does not let a late read of an earlier source disarm a ready successor of another source', async () => {
    const late = deferred<GetGitHubObservationTargetResponse>()
    const { record } = install((id) => (id === 'run-a' ? late.promise : Promise.resolve(notRecorded(sourceOf(id)))))
    const mounted = mountFor(A)
    act(() => mounted.result.current.show())
    act(() => mounted.result.current.reload())

    mounted.rerender({ source: B })
    await readyToRecord(mounted)
    expect(mounted.result.current.canRecord).toBe(true)

    await act(async () => late.resolve(notRecorded(A)))
    expect(mounted.result.current.canRecord).toBe(true)
    await act(async () => mounted.result.current.record())

    expect(record).toHaveBeenCalledTimes(1)
    expect(record.mock.calls[0][0]).toBe('run-b')
  })
})
