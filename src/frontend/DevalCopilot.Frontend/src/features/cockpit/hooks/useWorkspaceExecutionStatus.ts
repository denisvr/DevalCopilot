import { useCallback, useEffect, useRef } from 'react'
import { workspaceExecutionStatusClient } from '../../../api/clients'
import { toWorkspaceExecutionReading } from '../workspaceExecution'
import type { WorkspaceExecutionReadFailure, WorkspaceExecutionReading, WorkspaceExecutionState } from '../workspaceExecution'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'
import type { OwnedLifetime } from './useOwnedLifetime'

/** How long the selected project waits between two reads while it is mounted (ADR-0057): verification transitions emit no run event. */
export const WORKSPACE_EXECUTION_REFRESH_INTERVAL_MS = 5000

/** Each read's request is aborted after this long: a hung request can never keep an earlier success current. */
export const WORKSPACE_EXECUTION_REQUEST_TIMEOUT_MS = 10_000

/** How many reads in a row may fail or be refused before polling stops; a later hint, refresh or remount reads again. */
export const WORKSPACE_EXECUTION_MAX_CONSECUTIVE_FAILURES = 3

export interface UseWorkspaceExecutionStatusResult extends WorkspaceExecutionState {
  /** Whether further bounded reads are scheduled by timer. */
  polling: boolean
}

// The one request a lifetime has in flight: whether its transport is still unsettled, whether a trigger is waiting behind it, and how
// to abort it.
interface FlightState {
  owner: OwnedLifetime | null
  busy: boolean
  queued: boolean
  cancel: (() => void) | null
}

// Everything one project's visit owns: the validated answer or a failure, whether the first read settled, and the bounded polling state.
interface Frame {
  reading: WorkspaceExecutionReading | null
  failure: WorkspaceExecutionReadFailure | null
  settled: boolean
  readCount: number
  settledReads: number
  consecutiveFailures: number
}

const createFrame = (): Frame => ({
  reading: null,
  failure: null,
  settled: false,
  readCount: 0,
  settledReads: 0,
  consecutiveFailures: 0,
})

function isNotFound(caught: unknown): boolean {
  return typeof caught === 'object' && caught !== null && (caught as { status?: unknown }).status === 404
}

type ReadAnswer = { raw: unknown } | { failure: WorkspaceExecutionReadFailure }

/**
 * The request or its ten-second bound, whichever settles first; the loser is ignored. The read owns its actual transport request: when
 * the bound elapses the request is aborted, not merely abandoned, and `cancel` (the lifetime that owned the read ended) aborts it and
 * ends the bound at once, so no timer is left running. `answer` settles what is shown; `transport` settles only once the request itself
 * has, so the next read of the lifetime never starts while an earlier request is still in flight.
 */
function readWithin(projectId: string, milliseconds: number): { answer: Promise<ReadAnswer>; transport: Promise<void>; cancel: () => void } {
  const controller = new AbortController()
  let finish: (value: ReadAnswer) => void = () => undefined
  let timer: number | undefined
  let transport: Promise<void> = Promise.resolve()
  const answer = new Promise<ReadAnswer>((resolve) => {
    let settled = false
    finish = (value) => {
      if (!settled) {
        settled = true
        window.clearTimeout(timer)
        resolve(value)
      }
    }

    timer = window.setTimeout(() => {
      finish({ failure: 'timeout' })
      controller.abort()
    }, milliseconds)

    let request: Promise<unknown>
    try {
      request = workspaceExecutionStatusClient(controller.signal).readWorkspaceExecutionStatus(projectId)
    } catch (caught) {
      request = Promise.reject(caught)
    }

    transport = request.then(
      (raw) => finish({ raw }),
      (caught: unknown) => finish({ failure: isNotFound(caught) ? 'not-found' : 'read' }),
    )
  })

  return {
    answer,
    transport,
    cancel: () => {
      window.clearTimeout(timer)
      controller.abort()
    },
  }
}

/**
 * The workspace execution status of the selected project (ADR-0057), read-only: it writes nothing, starts nothing and is only ever an
 * additional veto for starting work. The project's visit owns everything it exposes: another project, or a return to an earlier one,
 * derives a fresh loading state and never exposes the previous reading, an older read can neither overwrite nor settle a newer one, and
 * a `refresh` retained from a replaced or unmounted visit starts no request.
 *
 * It reads once when the project is selected, again whenever `hint` changes (a durable hint such as the evidence refresh generation) or
 * `refresh` is called, and every five seconds while the project is mounted; timer polling stops after three consecutive failed reads
 * until a hint, a refresh or a remount. Reads never overlap: a trigger that arrives while one is pending is coalesced into exactly one
 * further read after its actual request settles. Each request is aborted after ten seconds and when the visit ends, so an abandoned
 * request never stays in flight. A pending re-read keeps the last validated reading, and a failed, timed-out, refused or incoherent
 * read masks it. Nothing is stored in the browser.
 */
export function useWorkspaceExecutionStatus(projectId: string | null, hint?: number): UseWorkspaceExecutionStatusResult {
  const owner = useOwnedLifetime(projectId)
  const [frame, commit] = useOwnedState(owner, createFrame)
  const { settledReads, consecutiveFailures, settled, readCount } = frame
  const flight = useRef<FlightState>({ owner: null, busy: false, queued: false, cancel: null })

  const refresh = useCallback(() => {
    if (!owner.isActive() || owner.key === null) {
      return
    }

    commit((previous) => ({ ...previous, readCount: previous.readCount + 1 }))
  }, [owner, commit])

  useEffect(() => {
    if (projectId === null) {
      return
    }

    if (flight.current.owner !== owner) {
      flight.current = { owner, busy: false, queued: false, cancel: null }
    }

    const state = flight.current
    if (state.busy) {
      // One request at a time: this trigger becomes exactly one further read once the pending one settles.
      state.queued = true
      return
    }

    state.busy = true
    const isCurrent = owner.begin('read')
    const settle = (answer: { reading: WorkspaceExecutionReading | null; failure: WorkspaceExecutionReadFailure | null }) => {
      if (isCurrent()) {
        commit((previous) => ({
          ...previous,
          reading: answer.reading,
          failure: answer.failure,
          settled: true,
          settledReads: previous.settledReads + 1,
          consecutiveFailures: answer.reading === null ? previous.consecutiveFailures + 1 : 0,
        }))
      }
    }

    const request = readWithin(projectId, WORKSPACE_EXECUTION_REQUEST_TIMEOUT_MS)
    state.cancel = request.cancel
    void request.answer.then((answer) => {
      if ('failure' in answer) {
        settle({ reading: null, failure: answer.failure })
        return
      }

      const decoded = toWorkspaceExecutionReading(answer.raw, projectId)
      settle(decoded === null ? { reading: null, failure: 'invalid' } : { reading: decoded, failure: null })
    })

    // The flight ends only when the actual request settled (answered, failed or aborted), never merely when its bound elapsed: a trigger
    // that arrived meanwhile becomes exactly one further read after it, so this lifetime never has two requests in flight.
    void request.transport.then(() => {
      state.busy = false
      state.cancel = null
      if (state.queued && owner.isActive()) {
        state.queued = false
        commit((previous) => ({ ...previous, readCount: previous.readCount + 1 }))
      }
    })
  }, [owner, commit, projectId, hint, readCount])

  // The lifetime ended (another project was selected or the view unmounted): its pending request is aborted and can no longer settle
  // anything, and its time bound stops with it.
  useEffect(
    () => () => {
      flight.current.cancel?.()
      flight.current.cancel = null
    },
    [owner],
  )

  const polling = projectId !== null && consecutiveFailures < WORKSPACE_EXECUTION_MAX_CONSECUTIVE_FAILURES
  useEffect(() => {
    if (!polling || settledReads === 0) {
      return
    }

    // One timer per settled read: the next read is scheduled only after the previous one settled, so reads never overlap.
    const timer = window.setTimeout(() => {
      commit((previous) => ({ ...previous, readCount: previous.readCount + 1 }))
    }, WORKSPACE_EXECUTION_REFRESH_INTERVAL_MS)
    return () => window.clearTimeout(timer)
  }, [polling, settledReads, commit])

  return {
    reading: projectId === null ? null : frame.reading,
    loading: projectId !== null && !settled,
    failure: projectId === null ? null : frame.failure,
    polling,
    refresh,
  }
}
