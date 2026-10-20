import { useCallback, useEffect, useRef } from 'react'
import { executionCapacityClient, executionCapacityLimitClient } from '../../../api/clients'
import { ApiException, ApiProblemDetails } from '../../../api/generated/api-client'
import { isExecutionCapacityLimit, toExecutionCapacityReading } from '../executionCapacity'
import type {
  ExecutionCapacityChange,
  ExecutionCapacityChangeOutcome,
  ExecutionCapacityChangePhase,
  ExecutionCapacityLimit,
  ExecutionCapacityReadFailure,
  ExecutionCapacityReading,
  ExecutionCapacityState,
} from '../executionCapacity'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'
import type { OwnedLifetime } from './useOwnedLifetime'

/** How long the host session waits between two reads while it is mounted (ADR-0058): verification transitions emit no run event. */
export const EXECUTION_CAPACITY_REFRESH_INTERVAL_MS = 5000

/** Each read's request, and the one explicit limit change's request, is aborted after this long: a hung request can never keep an earlier success current. */
export const EXECUTION_CAPACITY_REQUEST_TIMEOUT_MS = 10_000

/** How many reads in a row may fail or be refused before polling stops; a later hint, refresh or remount reads again. */
export const EXECUTION_CAPACITY_MAX_CONSECUTIVE_FAILURES = 3

export interface UseExecutionCapacityResult extends ExecutionCapacityState {
  /** Whether further bounded reads are scheduled by timer. */
  polling: boolean
  /** Where the explicit limit change of this lifetime is; always present (idle before any change). */
  change: ExecutionCapacityChange
  /**
   * Explicitly asks the host to record the limit one or two. It starts at most one write at a time, only while the lifetime is active
   * and no change is unconfirmed, never retries it, and a callback retained from an ended lifetime does nothing. It starts no work.
   */
  applyLimit: (limit: ExecutionCapacityLimit) => void
}

// The one request a lifetime has in flight and the limit change barrier it owns. `started` counts the reads this lifetime began, so a
// read that began before the write transport settled can be told from the first one that began after it (`confirmAfter`).
interface FlightState {
  owner: OwnedLifetime | null
  busy: boolean
  queued: boolean
  cancel: (() => void) | null
  started: number
  phase: ExecutionCapacityChangePhase
  confirmAfter: number
  cancelWrite: (() => void) | null
}

// Everything one host session's lifetime owns: the validated answer or a failure, whether the first read settled, the bounded polling
// state and the visible limit change barrier.
interface Frame {
  reading: ExecutionCapacityReading | null
  failure: ExecutionCapacityReadFailure | null
  settled: boolean
  readCount: number
  settledReads: number
  consecutiveFailures: number
  phase: ExecutionCapacityChangePhase
  outcome: ExecutionCapacityChangeOutcome | null
  requested: ExecutionCapacityLimit | null
}

const createFrame = (): Frame => ({
  reading: null,
  failure: null,
  settled: false,
  readCount: 0,
  settledReads: 0,
  consecutiveFailures: 0,
  phase: 'idle',
  outcome: null,
  requested: null,
})

const createFlight = (owner: OwnedLifetime | null): FlightState => ({
  owner,
  busy: false,
  queued: false,
  cancel: null,
  started: 0,
  phase: 'idle',
  confirmAfter: 0,
  cancelWrite: null,
})

/** The single owner key of the authenticated host session's lifetime: independent of any project or run selection. */
const HOST_SESSION = 'host-session'

type ReadAnswer = { raw: unknown } | { failure: ExecutionCapacityReadFailure }

/**
 * The request or its ten-second bound, whichever settles first; the loser is ignored. The read owns its actual transport request: when
 * the bound elapses the request is aborted, not merely abandoned, and `cancel` (the lifetime that owned the read ended) aborts it and
 * ends the bound at once, so no timer is left running. `answer` settles what is shown; `transport` settles only once the request itself
 * has, so the next read of the lifetime never starts while an earlier request is still in flight.
 */
function readWithin(milliseconds: number): { answer: Promise<ReadAnswer>; transport: Promise<void>; cancel: () => void } {
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
      request = executionCapacityClient(controller.signal).readExecutionCapacity()
    } catch (caught) {
      request = Promise.reject(caught)
    }

    transport = request.then(
      (raw) => finish({ raw }),
      () => finish({ failure: 'read' }),
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

/** A refusal the host answered, which proves nothing was changed. */
const DEFINITE_REFUSALS: readonly number[] = [400, 401, 403, 404, 409, 413, 415]
const BELOW_OWNERS_CODE = 'execution_capacity.limit_below_recorded_owners'

/**
 * How the write ended, judged only from its own transport. A declared or undeclared refusal that proves nothing changed is `refused`
 * (`belowOwners` for the lower-limit refusal); everything else (a success whose body is not a coherent reading, a timeout, an abort, a
 * network or server failure) is `unconfirmed`, because the change may already have committed and neither success nor rollback is inferred.
 */
function classifyWrite(raw: unknown): ExecutionCapacityChangeOutcome {
  return toExecutionCapacityReading(raw) === null ? 'unconfirmed' : 'applied'
}

function classifyWriteFailure(caught: unknown): ExecutionCapacityChangeOutcome {
  if (caught instanceof ApiProblemDetails) {
    if (caught.status === 409 && caught.errors?.[0]?.code === BELOW_OWNERS_CODE) {
      return 'belowOwners'
    }

    return caught.status !== undefined && DEFINITE_REFUSALS.includes(caught.status) ? 'refused' : 'unconfirmed'
  }

  if (ApiException.isApiException(caught)) {
    return DEFINITE_REFUSALS.includes(caught.status) ? 'refused' : 'unconfirmed'
  }

  return 'unconfirmed'
}

/**
 * The host's execution capacity (ADR-0058) and its one explicit limit change (ADR-0059). Reading writes nothing, starts nothing and is
 * only ever an additional veto for starting a new potentially mutating execution. The authenticated host session owns everything it
 * exposes, independently of which project is selected: becoming disabled or unmounting ends the lifetime, a return derives a fresh
 * loading state and never exposes the previous reading, an older read can neither overwrite nor settle a newer one, and a `refresh` or
 * `applyLimit` retained from an ended lifetime starts no request.
 *
 * It reads once when the session becomes ready, again whenever `hint` changes (a durable hint such as the evidence refresh generation)
 * or `refresh` is called, and every five seconds while mounted; timer polling stops after three consecutive failed reads until a hint, a
 * refresh or a remount. Reads never overlap: a trigger that arrives while one is pending is coalesced into exactly one further read after
 * its actual request settles. Each request is aborted after ten seconds and when the lifetime ends, so an abandoned request never stays
 * in flight. A pending re-read keeps the last validated reading as a hint, and a failed, timed-out, refused or incoherent read masks it.
 *
 * A limit change masks the reading for mutating-start purposes from its submission until a read that began after the write's actual
 * transport settled has settled: a read already pending before the change, a late answer or a stale callback restores nothing, and a
 * failed or incoherent confirmation keeps withholding until a valid current read. At most one write is in flight; it is aborted after
 * ten seconds and with its lifetime, its answer is never trusted as the new reading, and it is never retried. Nothing is stored in the browser.
 */
export function useExecutionCapacity(enabled: boolean, hint?: number): UseExecutionCapacityResult {
  const owner = useOwnedLifetime(enabled ? HOST_SESSION : null)
  const [frame, commit] = useOwnedState(owner, createFrame)
  const { settledReads, consecutiveFailures, settled, readCount } = frame
  const flight = useRef<FlightState>(createFlight(null))

  const flightOf = useCallback((lifetime: OwnedLifetime): FlightState => {
    if (flight.current.owner !== lifetime) {
      flight.current = createFlight(lifetime)
    }

    return flight.current
  }, [])

  const refresh = useCallback(() => {
    if (!owner.isActive() || owner.key === null) {
      return
    }

    commit((previous) => ({ ...previous, readCount: previous.readCount + 1 }))
  }, [owner, commit])

  const applyLimit = useCallback(
    (limit: ExecutionCapacityLimit) => {
      if (!isExecutionCapacityLimit(limit) || !owner.isActive() || owner.key === null) {
        return
      }

      const state = flightOf(owner)
      if (state.phase !== 'idle') {
        return
      }

      // From this point until a later read settles, the old reading is masked and no second write can start.
      state.phase = 'applying'
      commit((previous) => ({ ...previous, phase: 'applying', outcome: null, requested: limit }))
      const isCurrent = owner.begin('write')
      const controller = new AbortController()
      const timer = window.setTimeout(() => controller.abort(), EXECUTION_CAPACITY_REQUEST_TIMEOUT_MS)
      state.cancelWrite = () => {
        window.clearTimeout(timer)
        controller.abort()
      }

      let request: Promise<unknown>
      try {
        request = executionCapacityLimitClient(controller.signal).changeExecutionCapacityLimit(limit)
      } catch (caught) {
        request = Promise.reject(caught)
      }

      // The write has ended only when its actual transport settled (answered, failed or aborted).
      void request
        .then(classifyWrite, classifyWriteFailure)
        .then((outcome) => {
          window.clearTimeout(timer)
          state.cancelWrite = null
          if (!isCurrent()) {
            return
          }

          state.phase = 'confirming'
          state.confirmAfter = state.started
          commit((previous) => ({ ...previous, phase: 'confirming', outcome }))
          // Every trigger coalesced while the write was unsettled becomes this one fresh read: behind an older read still in
          // flight (started when that transport settles) or right now, never both.
          if (state.busy) {
            state.queued = true
          } else {
            state.queued = false
            commit((previous) => ({ ...previous, readCount: previous.readCount + 1 }))
          }
        })
    },
    [owner, commit, flightOf],
  )

  useEffect(() => {
    if (!enabled) {
      return
    }

    const state = flightOf(owner)
    if (state.busy || state.phase === 'applying') {
      // One request at a time, and none while the write is unsettled (ADR-0059): this trigger is coalesced into exactly one further
      // read, started once the pending one has settled and, around a write, only after the write has.
      state.queued = true
      return
    }

    state.busy = true
    const index = ++state.started
    const isCurrent = owner.begin('read')
    const settle = (answer: { reading: ExecutionCapacityReading | null; failure: ExecutionCapacityReadFailure | null }) => {
      if (!isCurrent()) {
        return
      }

      if (state.phase === 'applying' || (state.phase === 'confirming' && index <= state.confirmAfter)) {
        // A read that began before the write's transport settled can say nothing about the change: it neither restores an earlier
        // reading, confirms the change nor rearms polling; the one confirming read follows the write.
        return
      }

      const confirmed = state.phase === 'confirming'
      if (confirmed) {
        state.phase = 'idle'
      }

      commit((previous) => ({
        ...previous,
        reading: answer.reading,
        failure: answer.failure,
        settled: true,
        settledReads: previous.settledReads + 1,
        consecutiveFailures: answer.reading === null ? previous.consecutiveFailures + 1 : 0,
        phase: confirmed ? 'idle' : previous.phase,
      }))
    }

    const request = readWithin(EXECUTION_CAPACITY_REQUEST_TIMEOUT_MS)
    state.cancel = request.cancel
    void request.answer.then((answer) => {
      if ('failure' in answer) {
        settle({ reading: null, failure: answer.failure })
        return
      }

      const decoded = toExecutionCapacityReading(answer.raw)
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
  }, [owner, commit, enabled, hint, readCount, flightOf])

  // The lifetime ended (the session stopped being ready or the host view unmounted): its pending request and any write in flight are
  // aborted and can no longer settle anything, and their time bounds stop with it.
  useEffect(
    () => () => {
      flight.current.cancel?.()
      flight.current.cancel = null
      flight.current.cancelWrite?.()
      flight.current.cancelWrite = null
    },
    [owner],
  )

  const polling = enabled && consecutiveFailures < EXECUTION_CAPACITY_MAX_CONSECUTIVE_FAILURES
  useEffect(() => {
    if (!polling || settledReads === 0) {
      return
    }

    // One timer per settled read: the next read is scheduled only after the previous one settled, so reads never overlap.
    const timer = window.setTimeout(() => {
      commit((previous) => ({ ...previous, readCount: previous.readCount + 1 }))
    }, EXECUTION_CAPACITY_REFRESH_INTERVAL_MS)
    return () => window.clearTimeout(timer)
  }, [polling, settledReads, commit])

  const masked = !enabled || frame.phase !== 'idle'
  return {
    reading: masked ? null : frame.reading,
    loading: enabled && !settled,
    failure: enabled ? frame.failure : null,
    polling,
    refresh,
    change: enabled ? { phase: frame.phase, outcome: frame.outcome, requested: frame.requested } : { phase: 'idle', outcome: null, requested: null },
    applyLimit,
  }
}
