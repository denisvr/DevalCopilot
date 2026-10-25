import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { localExecutionOwnershipReleaseCommandClient, localExecutionOwnershipReleaseReadClient } from '../../../api/clients'
import { ApiProblemDetails } from '../../../api/generated/api-client'
import type { LocalExecutionReleaseTarget } from '../localExecutionOwnershipRelease'
import {
  RELEASE_BUSY,
  RELEASE_UNKNOWN_OUTCOME,
} from '../localExecutionOwnershipReleaseCopy'
import {
  RELEASE_CONFIRMATION,
  RELEASE_OBSERVATION_ID,
  RELEASE_RUN,
  RELEASE_STEP_ID,
  availableReleaseFor,
  notEligibleReleaseFor,
  releaseTargetFor,
  releasedReleaseFor,
} from '../localExecutionOwnershipReleaseFixture'
import { localGuid } from '../localExecutionSequenceFixture'
import { BOUNDED_COMMAND_TIMEOUT_MS } from './boundedCommand'
import { RELEASE_CONFIRMATION_LIFETIME_MS, useLocalExecutionOwnershipRelease } from './useLocalExecutionOwnershipRelease'

vi.mock('../../../api/clients', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../../api/clients')>()),
  localExecutionOwnershipReleaseReadClient: vi.fn(),
  localExecutionOwnershipReleaseCommandClient: vi.fn(),
}))

const readSignals: AbortSignal[] = []
const commandSignals: AbortSignal[] = []

function install(
  read: (runId: string, stepId: string, signal: AbortSignal) => Promise<unknown>,
  command: (runId: string, stepId: string, signal: AbortSignal) => Promise<unknown> = () => Promise.resolve(releasedReleaseFor()),
) {
  const reads = vi.fn()
  const commands = vi.fn()
  vi.mocked(localExecutionOwnershipReleaseReadClient).mockImplementation((signal?: AbortSignal) => {
    readSignals.push(signal!)
    return {
      readRelease: (runId: string, stepId: string) => {
        reads(runId, stepId)
        return read(runId, stepId, signal!)
      },
    } as unknown as ReturnType<typeof localExecutionOwnershipReleaseReadClient>
  })
  vi.mocked(localExecutionOwnershipReleaseCommandClient).mockImplementation((signal?: AbortSignal) => {
    commandSignals.push(signal!)
    return {
      releaseRaw: (runId: string, stepId: string, candidate: unknown) => {
        commands(runId, stepId, candidate)
        return command(runId, stepId, signal!)
      },
    } as unknown as ReturnType<typeof localExecutionOwnershipReleaseCommandClient>
  })
  return { reads, commands }
}

interface Props {
  runId: string | null
  targets: readonly LocalExecutionReleaseTarget[] | null
}

const STEP_A = releaseTargetFor()
const STEP_B = releaseTargetFor({ stepId: localGuid(101), ordinal: 1, stepKind: 'CodeReview', passNumber: 1, operationId: localGuid(201) })

function mounted(beginCommand: () => { settled: () => void } | null = () => ({ settled: vi.fn() }), initial: Props = { runId: RELEASE_RUN, targets: [STEP_A] }) {
  const reader = { beginCommand }
  return renderHook((props: Props) => useLocalExecutionOwnershipRelease(props.runId, props.targets, reader), { initialProps: initial })
}

const refused = (status: number, code: string) =>
  ApiProblemDetails.fromJS({ status, errors: [{ code, detail: 'server text that must never be shown' }], traceId: 't' })

const never = (signal: AbortSignal) => new Promise<unknown>((_, reject) => signal.addEventListener('abort', () => reject(new DOMException('a', 'AbortError'))))

async function openAndSelect(result: { current: ReturnType<typeof useLocalExecutionOwnershipRelease> }, stepId = RELEASE_STEP_ID) {
  await act(async () => result.current.show())
  await act(async () => result.current.select(stepId))
}

describe('useLocalExecutionOwnershipRelease', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    readSignals.length = 0
    commandSignals.length = 0
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('reads nothing until the owner opens the region and chooses one step, and never polls', async () => {
    const { reads } = install(() => Promise.resolve(availableReleaseFor()))
    const { result } = mounted()
    await act(async () => vi.advanceTimersByTimeAsync(60_000))
    expect(reads).not.toHaveBeenCalled()

    await act(async () => result.current.show())
    expect(result.current.open).toBe(true)
    expect(reads).not.toHaveBeenCalled()

    await act(async () => result.current.select(RELEASE_STEP_ID))
    expect(reads).toHaveBeenCalledTimes(1)
    expect(reads).toHaveBeenCalledWith(RELEASE_RUN, RELEASE_STEP_ID)
    expect(result.current.answer).toMatchObject({ status: 'Available' })
    await act(async () => vi.advanceTimersByTimeAsync(RELEASE_CONFIRMATION_LIFETIME_MS - 1000))
    expect(reads).toHaveBeenCalledTimes(1)
  })

  it('ends a read that never answers at ten seconds: the actual request is aborted, a fixed read failure settles and nothing retries', async () => {
    const { reads } = install((_run, _step, signal) => never(signal))
    const { result } = mounted()
    await openAndSelect(result)
    expect(result.current.reading).toBe(true)

    await act(async () => vi.advanceTimersByTimeAsync(BOUNDED_COMMAND_TIMEOUT_MS - 1))
    expect(result.current.reading).toBe(true)
    expect(readSignals[0].aborted).toBe(false)
    await act(async () => vi.advanceTimersByTimeAsync(1))

    expect(readSignals[0].aborted).toBe(true)
    expect(result.current.reading).toBe(false)
    expect(result.current.readFailure).toBe('read')
    expect(result.current.answer).toBeNull()
    await act(async () => vi.advanceTimersByTimeAsync(120_000))
    expect(reads).toHaveBeenCalledTimes(1)
  })

  it('a transport that ignores the abort cannot keep the read pending or restore a confirmation by answering late', async () => {
    let answer: (value: unknown) => void = () => undefined
    const { commands } = install(() => new Promise<unknown>((resolve) => (answer = resolve)))
    const { result } = mounted(() => ({ settled: vi.fn() }))
    await openAndSelect(result)

    await act(async () => vi.advanceTimersByTimeAsync(BOUNDED_COMMAND_TIMEOUT_MS))
    expect(result.current.readFailure).toBe('read')
    expect(result.current.reading).toBe(false)

    await act(async () => answer(availableReleaseFor()))
    expect(result.current.answer).toBeNull()
    expect(result.current.readFailure).toBe('read')
    await act(async () => result.current.confirm())
    expect(commands).not.toHaveBeenCalled()
  })

  it('a read that answers before ten seconds is never failed by its own bound afterwards', async () => {
    install(() => Promise.resolve(availableReleaseFor()))
    const { result } = mounted()
    await openAndSelect(result)
    expect(result.current.answer).toMatchObject({ status: 'Available' })

    await act(async () => vi.advanceTimersByTimeAsync(BOUNDED_COMMAND_TIMEOUT_MS * 3))

    expect(result.current.readFailure).toBeNull()
    expect(result.current.answer).toMatchObject({ status: 'Available' })
  })

  it('a reload after a failed read starts a new bounded read and a late answer of the earlier read cannot settle it', async () => {
    const pending: { resolve: (value: unknown) => void }[] = []
    install(() => new Promise<unknown>((resolve) => pending.push({ resolve })))
    const { result } = mounted()
    await openAndSelect(result)
    await act(async () => vi.advanceTimersByTimeAsync(BOUNDED_COMMAND_TIMEOUT_MS))
    expect(result.current.readFailure).toBe('read')

    await act(async () => result.current.reload())
    expect(result.current.reading).toBe(true)
    await act(async () => pending[0].resolve(availableReleaseFor({}, { observationId: localGuid(777) })))
    expect(result.current.reading).toBe(true)
    expect(result.current.answer).toBeNull()
    await act(async () => pending[1].resolve(availableReleaseFor()))
    expect(result.current.answer).toMatchObject({ status: 'Available', candidate: { observationId: RELEASE_OBSERVATION_ID } })
  })

  it('ignores a step the accepted status does not offer, and every choice while the status is unknown', async () => {
    const { reads } = install(() => Promise.resolve(availableReleaseFor()))
    const { result, rerender } = mounted()
    await act(async () => result.current.show())
    await act(async () => result.current.select(localGuid(999)))
    expect(result.current.selected).toBeNull()

    rerender({ runId: RELEASE_RUN, targets: null })
    await act(async () => result.current.select(RELEASE_STEP_ID))
    expect(result.current.selected).toBeNull()
    expect(reads).not.toHaveBeenCalled()
  })

  it('words each failed read and never retries it on its own', async () => {
    const outcomes: [unknown, string][] = [
      [() => Promise.reject(new Error('socket closed')), 'read'],
      [() => Promise.reject(refused(404, 'local_execution_ownership_release.not_found')), 'not-found'],
      [() => Promise.resolve({ not: 'an answer' }), 'invalid'],
      [() => Promise.resolve(availableReleaseFor({ runId: 'run-2' })), 'invalid'],
    ]
    for (const [read, failure] of outcomes) {
      const { reads } = install(read as () => Promise<unknown>)
      const { result, unmount } = mounted()
      await openAndSelect(result)
      await act(async () => vi.advanceTimersByTimeAsync(60_000))
      expect(result.current.readFailure).toBe(failure)
      expect(result.current.answer).toBeNull()
      expect(reads).toHaveBeenCalledTimes(1)
      unmount()
    }
  })

  it('shows a not eligible answer and a received receipt without offering a confirmation', async () => {
    install(() => Promise.resolve(notEligibleReleaseFor('already_free')))
    const { result } = mounted()
    await openAndSelect(result)
    expect(result.current.answer).toMatchObject({ status: 'NotEligible', reasonCode: 'already_free' })

    install(() => Promise.resolve(releasedReleaseFor()))
    const released = mounted()
    await openAndSelect(released.result)
    await act(async () => released.result.current.confirm())
    expect(vi.mocked(localExecutionOwnershipReleaseCommandClient)).not.toHaveBeenCalled()
  })

  it('sends one release for exactly the four confirmed members, with the barrier, and consumes the confirmation', async () => {
    const { commands } = install(() => Promise.resolve(availableReleaseFor()))
    const settled = vi.fn()
    const beginCommand = vi.fn(() => ({ settled }))
    const { result } = mounted(beginCommand)
    await openAndSelect(result)

    await act(async () => result.current.confirm())
    await act(async () => result.current.confirm())

    expect(beginCommand).toHaveBeenCalledTimes(1)
    expect(commands).toHaveBeenCalledTimes(1)
    expect(commands).toHaveBeenCalledWith(RELEASE_RUN, RELEASE_STEP_ID, expect.objectContaining({
      observationId: RELEASE_OBSERVATION_ID,
      confirmationVersion: '1',
      confirmationSha256: RELEASE_CONFIRMATION,
      confirmationExpiresAtUtc: '2026-10-09T10:07:30.0000000Z',
    }))
    expect(settled).toHaveBeenCalledTimes(1)
    expect(result.current.submission).toMatchObject({ phase: 'released', release: { eventSequence: '42' } })
    await act(async () => result.current.confirm())
    expect(commands).toHaveBeenCalledTimes(1)
  })

  it('a second activation within the same batch cannot send a second request', async () => {
    const { commands } = install(() => Promise.resolve(availableReleaseFor()))
    const { result } = mounted(() => ({ settled: vi.fn() }))
    await openAndSelect(result)

    await act(async () => {
      result.current.confirm()
      result.current.confirm()
    })

    expect(commands).toHaveBeenCalledTimes(1)
  })

  it('a confirm callback retained from an ended visit sends nothing for the newer visit of the same step', async () => {
    const { commands } = install(() => Promise.resolve(availableReleaseFor()))
    const { result } = mounted(() => ({ settled: vi.fn() }))
    await openAndSelect(result)
    const retained = result.current.confirm

    await act(async () => result.current.reload())
    expect(result.current.answer).toMatchObject({ status: 'Available' })
    await act(async () => retained())

    expect(commands).not.toHaveBeenCalled()
    expect(result.current.submission).toEqual({ phase: 'idle', message: null })
    await act(async () => result.current.confirm())
    expect(commands).toHaveBeenCalledTimes(1)
  })

  it('sends nothing and consumes nothing when the status barrier cannot start', async () => {
    const { commands } = install(() => Promise.resolve(availableReleaseFor()))
    let barrier = false
    const settled = vi.fn()
    const { result } = mounted(() => (barrier ? { settled } : null))
    await openAndSelect(result)

    await act(async () => result.current.confirm())
    expect(commands).not.toHaveBeenCalled()
    expect(result.current.submission).toEqual({ phase: 'idle', message: RELEASE_BUSY })

    barrier = true
    await act(async () => result.current.confirm())
    expect(commands).toHaveBeenCalledTimes(1)
    expect(result.current.submission.phase).toBe('released')
  })

  it('words a refusal from its stable code only, never resends, and needs a new read for a new confirmation', async () => {
    const { reads, commands } = install(
      () => Promise.resolve(availableReleaseFor()),
      () => Promise.reject(refused(409, 'local_execution_ownership_release.confirmation_stale')),
    )
    const settled = vi.fn()
    const { result } = mounted(() => ({ settled }))
    await openAndSelect(result)

    await act(async () => result.current.confirm())
    await act(async () => vi.advanceTimersByTimeAsync(60_000))

    expect(commands).toHaveBeenCalledTimes(1)
    expect(settled).toHaveBeenCalledTimes(1)
    expect(result.current.submission.phase).toBe('refused')
    expect(result.current.submission.message).toContain('expired, or the recorded facts changed')
    expect(result.current.submission.message).not.toContain('server text')
    await act(async () => result.current.confirm())
    expect(commands).toHaveBeenCalledTimes(1)

    await act(async () => result.current.reload())
    expect(reads).toHaveBeenCalledTimes(2)
    expect(result.current.submission).toEqual({ phase: 'idle', message: null })
    await act(async () => result.current.confirm())
    expect(commands).toHaveBeenCalledTimes(2)
  })

  it.each([
    ['a transport failure', () => Promise.reject(new Error('socket closed'))],
    ['a server error', () => Promise.reject(refused(500, 'local_execution_ownership_release.persistence_unresolved'))],
    ['a foreign answer', () => Promise.resolve(releasedReleaseFor({ runId: 'run-2' }))],
    ['a non-released answer', () => Promise.resolve(availableReleaseFor())],
  ])('treats %s as an unknown outcome, never infers a rollback and never resends', async (_name, command) => {
    const { commands } = install(() => Promise.resolve(availableReleaseFor()), command)
    const settled = vi.fn()
    const { result } = mounted(() => ({ settled }))
    await openAndSelect(result)

    await act(async () => result.current.confirm())
    await act(async () => vi.advanceTimersByTimeAsync(60_000))

    expect(result.current.submission).toEqual({ phase: 'unknown', message: RELEASE_UNKNOWN_OUTCOME })
    expect(commands).toHaveBeenCalledTimes(1)
    expect(settled).toHaveBeenCalledTimes(1)
  })

  it('aborts the actual request after ten seconds and reports an unknown outcome', async () => {
    install(() => Promise.resolve(availableReleaseFor()), (_run, _step, signal) => never(signal))
    const { result } = mounted()
    await openAndSelect(result)

    await act(async () => result.current.confirm())
    expect(result.current.submission.phase).toBe('submitting')
    await act(async () => vi.advanceTimersByTimeAsync(BOUNDED_COMMAND_TIMEOUT_MS))

    expect(commandSignals[0].aborted).toBe(true)
    expect(result.current.submission.phase).toBe('unknown')
  })

  it('ends the confirmation when its lifetime passes and sends nothing afterwards', async () => {
    const { commands } = install(() => Promise.resolve(availableReleaseFor()))
    const { result } = mounted()
    await openAndSelect(result)
    expect(result.current.expired).toBe(false)

    await act(async () => vi.advanceTimersByTimeAsync(RELEASE_CONFIRMATION_LIFETIME_MS))
    expect(result.current.expired).toBe(true)
    await act(async () => result.current.confirm())
    expect(commands).not.toHaveBeenCalled()

    await act(async () => result.current.reload())
    expect(result.current.expired).toBe(false)
    await act(async () => result.current.confirm())
    expect(commands).toHaveBeenCalledTimes(1)
  })

  it('never lets an older read answer a newer visit of the same step or of another step (A-B-A)', async () => {
    const pending: { resolve: (value: unknown) => void; step: string }[] = []
    install((_run, step) => new Promise<unknown>((resolve) => pending.push({ resolve, step })))
    const { result } = mounted(undefined, { runId: RELEASE_RUN, targets: [STEP_A, STEP_B] })
    await act(async () => result.current.show())

    await act(async () => result.current.select(STEP_A.stepId))
    await act(async () => result.current.select(STEP_B.stepId))
    await act(async () => result.current.select(STEP_A.stepId))
    expect(pending.map((entry) => entry.step)).toEqual([STEP_A.stepId, STEP_B.stepId, STEP_A.stepId])
    expect(readSignals.map((signal) => signal.aborted)).toEqual([true, true, false])

    await act(async () => pending[0].resolve(availableReleaseFor({}, { observationId: localGuid(777) })))
    expect(result.current.answer).toBeNull()
    expect(result.current.reading).toBe(true)
    await act(async () => pending[2].resolve(availableReleaseFor()))
    expect(result.current.answer).toMatchObject({ status: 'Available', candidate: { observationId: RELEASE_OBSERVATION_ID } })
  })

  it('drops everything of another run and of an earlier visit to the same run, and aborts what was in flight', async () => {
    const { reads } = install(() => Promise.resolve(availableReleaseFor()))
    const { result, rerender } = mounted()
    await openAndSelect(result)
    expect(result.current.answer).not.toBeNull()

    rerender({ runId: 'run-2', targets: [STEP_A] })
    expect(result.current.open).toBe(false)
    expect(result.current.selected).toBeNull()
    expect(result.current.answer).toBeNull()

    rerender({ runId: RELEASE_RUN, targets: [STEP_A] })
    expect(result.current.open).toBe(false)
    expect(result.current.answer).toBeNull()
    expect(reads).toHaveBeenCalledTimes(1)
  })

  it('ends a visit whose operation the accepted status no longer offers, but keeps it while the status is unknown', async () => {
    install(() => Promise.resolve(availableReleaseFor()))
    const { result, rerender } = mounted()
    await openAndSelect(result)

    rerender({ runId: RELEASE_RUN, targets: null })
    expect(result.current.selected).not.toBeNull()
    expect(result.current.answer).not.toBeNull()

    rerender({ runId: RELEASE_RUN, targets: [] })
    expect(result.current.selected).toBeNull()
    expect(result.current.answer).toBeNull()
  })

  it('keeps an in-flight release and its outcome while the status is masked, and settles the barrier exactly once', async () => {
    let resolveCommand: (value: unknown) => void = () => undefined
    install(() => Promise.resolve(availableReleaseFor()), () => new Promise<unknown>((resolve) => (resolveCommand = resolve)))
    const settled = vi.fn()
    const { result, rerender } = mounted(() => ({ settled }))
    await openAndSelect(result)
    await act(async () => result.current.confirm())

    rerender({ runId: RELEASE_RUN, targets: null })
    expect(result.current.submission.phase).toBe('submitting')
    await act(async () => resolveCommand(releasedReleaseFor()))
    rerender({ runId: RELEASE_RUN, targets: [releaseTargetFor({ releasedInStatus: true })] })

    expect(result.current.submission.phase).toBe('released')
    expect(settled).toHaveBeenCalledTimes(1)
  })

  it('refuses to close, choose, clear or reload while the release is in flight', async () => {
    install(() => Promise.resolve(availableReleaseFor()), (_run, _step, signal) => never(signal))
    const { result } = mounted(undefined, { runId: RELEASE_RUN, targets: [STEP_A, STEP_B] })
    await openAndSelect(result)
    await act(async () => result.current.confirm())

    await act(async () => result.current.close())
    await act(async () => result.current.select(STEP_B.stepId))
    await act(async () => result.current.clear())
    await act(async () => result.current.reload())

    expect(result.current.open).toBe(true)
    expect(result.current.selected?.stepId).toBe(STEP_A.stepId)
    expect(result.current.submission.phase).toBe('submitting')
    expect(commandSignals[0].aborted).toBe(false)
  })

  it('aborts a request still in flight when the hook unmounts, and a retained callback sends nothing afterwards', async () => {
    const { commands } = install(() => Promise.resolve(availableReleaseFor()), (_run, _step, signal) => never(signal))
    const settled = vi.fn()
    const { result, unmount } = mounted(() => ({ settled }))
    await openAndSelect(result)
    const retained = result.current.confirm
    await act(async () => retained())
    expect(commands).toHaveBeenCalledTimes(1)

    unmount()
    await act(async () => vi.advanceTimersByTimeAsync(1))

    expect(commandSignals[0].aborted).toBe(true)
    expect(settled).toHaveBeenCalledTimes(1)
    retained()
    expect(commands).toHaveBeenCalledTimes(1)
  })

  it('closing discards the selection, the read and the confirmation, and a retained show of another opening cannot reopen it', async () => {
    const { reads } = install(() => Promise.resolve(availableReleaseFor()))
    const { result } = mounted()
    await openAndSelect(result)
    const retainedClose = result.current.close

    await act(async () => result.current.close())
    expect(result.current.open).toBe(false)
    expect(result.current.answer).toBeNull()

    await act(async () => result.current.show())
    expect(result.current.selected).toBeNull()
    await act(async () => retainedClose())
    expect(result.current.open).toBe(true)
    expect(reads).toHaveBeenCalledTimes(1)
  })

  it('sends nothing without a run', async () => {
    const { reads } = install(() => Promise.resolve(availableReleaseFor()))
    const { result } = mounted(undefined, { runId: null, targets: [STEP_A] })
    await act(async () => result.current.show())
    await act(async () => result.current.select(RELEASE_STEP_ID))
    expect(result.current.open).toBe(false)
    expect(reads).not.toHaveBeenCalled()
  })
})
