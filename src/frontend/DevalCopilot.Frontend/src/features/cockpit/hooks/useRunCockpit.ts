import { useCallback, useEffect, useRef } from 'react'
import type { GetRunCockpitResponse, RunEventResponse } from '../../../api/clients'
import { runCockpitClient, runEventsClient } from '../../../api/clients'
import { createRunNotificationConnection, type RunAdvancedNotification } from '../../../api/runNotifications'
import type { CollaborationCard, ConnectionState } from '../types'
import { parseEventSummary } from '../parseEventSummary'
import { toParticipantIdentity } from '../participantIdentity'
import { toUtcText } from '../utcText'
import { useOwnedLifetime, useOwnedState, type OwnedLifetime } from './useOwnedLifetime'

interface UseRunCockpitResult {
  cockpit: GetRunCockpitResponse | null
  cards: CollaborationCard[]
  connection: ConnectionState
  loading: boolean
  error: string | null
  // A transient failure to synchronize durable state after the transport is already
  // connected (post-connect catch-up, a reconnect, or a notification-triggered refresh).
  // Distinct from `error`, which means the run could not be loaded at all: this means the
  // transport is up but the cockpit may be showing stale data until the next successful
  // catch-up clears it.
  syncError: string | null
  // Re-queries the authoritative cockpit and event timeline for the current run right now,
  // without waiting for a notification (e.g. after a configuration change that emits no run
  // event). Resolves true only when a pass started at or after this call completed for the still-
  // current run; false on failure or if the run changed meanwhile. Never rejects.
  refresh: () => Promise<boolean>
}

// One instance is created fresh for each effect run (initial mount, a React Strict Mode
// double-invoke, or a runId change) and is never shared with any other generation. A stale
// generation's in-flight fetch or its own cleanup can therefore never clear or corrupt a
// newer generation's coordination flags — each generation owns its own inFlight/pending
// guard and its own event-sequence cursor.
interface CatchUpState {
  cursor: number
  inFlight: boolean
  pending: boolean
  // Whether the still-queued pending pass must mark the connection live on success (or,
  // symmetrically, report a failure through `syncError` rather than the fatal `error`). A
  // call that arrives while another is in flight only records this and returns; the loop
  // below picks it up for its next pass. OR-ing it in (rather than overwriting) means a
  // marksLive=true request is never dropped by coalescing with an earlier marksLive=false
  // one still in flight (e.g. the pre-connect initial fetch).
  pendingMarksLive: boolean
  // Same idea for an explicit refresh pass: judged per pass actually executed, and always weaker
  // than marksLive (a refresh never claims the live transport is up or down).
  pendingRefresh: boolean
  // Callers of refresh() awaiting the end of the whole coalesced loop (including any pass queued
  // behind an already-in-flight one, which is what guarantees a post-call fetch).
  waiters: Array<(ok: boolean) => void>
}

interface ActiveCatchUp {
  owner: OwnedLifetime
  runId: string
  generation: number
  state: CatchUpState
}

const REFRESH_FAILURE_MESSAGE = 'The cockpit could not be refreshed after your change.'
const WRONG_RUN_MESSAGE = 'The host answered with a different run than the one selected.'

// Everything the hook exposes about one selection. It belongs to that selection's lifetime only: a
// frame written for any other lifetime is never shown, so the committed render of a new selection
// starts from the loading state instead of from the previous run's snapshot.
interface CockpitFrame {
  cockpit: GetRunCockpitResponse | null
  cards: CollaborationCard[]
  connection: ConnectionState
  loading: boolean
  error: string | null
  syncError: string | null
}

function createFrame(runId: string | null): CockpitFrame {
  return { cockpit: null, cards: [], connection: 'connecting', loading: runId !== null, error: null, syncError: null }
}

function toCard(event: RunEventResponse): CollaborationCard {
  return {
    sequence: event.sequence ?? 0,
    id: event.id ?? '',
    attemptId: event.attemptId ?? null,
    eventType: event.eventType ?? '',
    actor: toParticipantIdentity(event.actor),
    summary: parseEventSummary(event.payloadJson ?? '', event.eventType),
    occurredAtUtc: toUtcText(event.occurredAtUtc),
  }
}

// Final defensive invariant: even if two fetches ever end up applying overlapping event
// ranges, an event already held by sequence number is never appended twice.
function mergeBySequence(previous: CollaborationCard[], nextEvents: RunEventResponse[]): CollaborationCard[] {
  const existingSequences = new Set(previous.map((card) => card.sequence))
  const additions = nextEvents.filter((event) => !existingSequences.has(event.sequence ?? 0)).map(toCard)
  return additions.length > 0 ? [...previous, ...additions] : previous
}

function toMessage(caught: unknown): string {
  return caught instanceof Error ? caught.message : 'Failed to synchronize the run.'
}

/**
 * Loads the durable cockpit projection and event timeline for one run, then keeps both
 * current through SignalR notifications. SignalR only announces that state advanced;
 * every refresh re-queries the authoritative API by sequence cursor, so a missed or
 * duplicated notification cannot create duplicate cards or stale-looking success.
 *
 * Everything exposed belongs to the committed selection lifetime. Selecting another run (or
 * none), returning to an earlier one, and unmounting each end the previous lifetime: its
 * snapshot, cards, connection and errors are never shown for the new selection, and its late
 * responses, notifications, refresh callbacks and waiters can no longer change anything.
 */
export function useRunCockpit(runId: string | null): UseRunCockpitResult {
  const owner = useOwnedLifetime(runId)
  const [frame, commit] = useOwnedState(owner, createFrame)
  // Identifies the current effect run. Bumped both when a newer run starts AND in this
  // effect's own cleanup (see below), so a plain unmount — where no newer generation is
  // ever created — still invalidates the generation immediately, rather than leaving it
  // matching forever.
  const generationRef = useRef(0)
  const activeRef = useRef<ActiveCatchUp | null>(null)

  // A single run of this loop can serve several coalesced requests (the pre-connect load,
  // then a post-connect/reconnect/notification refresh queued while it was still in
  // flight). `marksLive` — and therefore whether a failure is fatal (`error`) or transient
  // (`syncError` + not-live) — is judged per PASS actually executed, tracked via
  // `currentMarksLive`/`state.pendingMarksLive`, never by whichever caller happened to
  // start the loop. All classification and state updates happen inside this function; it
  // never rejects, so no caller needs its own `.catch()`.
  const fetchCatchUp = useCallback(
    async (
      currentRunId: string,
      generation: number,
      state: CatchUpState,
      marksLive: boolean,
      refresh = false,
      waiter?: (ok: boolean) => void,
    ) => {
      // The owner ends in the layout-effect cleanup of the commit that replaces it, before the passive
      // cleanup that retires the generation: an entry point reached in between (a consumer's layout
      // effect, a late notification) must already find the lifetime over.
      const isLive = () => owner.isActive() && generationRef.current === generation
      if (!isLive()) {
        waiter?.(false)
        return
      }

      if (waiter) {
        state.waiters.push(waiter)
      }

      if (state.inFlight) {
        state.pending = true
        state.pendingMarksLive = state.pendingMarksLive || marksLive
        state.pendingRefresh = state.pendingRefresh || refresh
        return
      }

      state.inFlight = true
      let currentMarksLive = marksLive
      let currentRefresh = refresh
      let succeeded = false
      try {
        do {
          state.pending = false
          state.pendingMarksLive = false
          state.pendingRefresh = false

          const [nextCockpit, nextEvents] = await Promise.all([
            runCockpitClient().getRunCockpit(currentRunId),
            runEventsClient().getRunEvents(currentRunId, state.cursor),
          ])

          if (!isLive()) {
            return
          }

          // A response that names another run is never this selection's cockpit.
          if (nextCockpit.runId !== currentRunId) {
            throw new Error(WRONG_RUN_MESSAGE)
          }

          if (nextEvents.length > 0) {
            state.cursor = Math.max(state.cursor, ...nextEvents.map((event) => event.sequence ?? 0))
          }
          const live = currentMarksLive
          const refreshed = currentRefresh
          commit((previous) => ({
            ...previous,
            loading: false,
            cockpit: nextCockpit,
            cards: nextEvents.length > 0 ? mergeBySequence(previous.cards, nextEvents) : previous.cards,
            ...(live
              ? { connection: 'live' as const, syncError: null }
              : refreshed
                ? { syncError: null }
                : { error: null }),
          }))

          currentMarksLive = state.pendingMarksLive
          currentRefresh = state.pendingRefresh
        } while (state.pending && isLive())
        succeeded = isLive()
      } catch (caught) {
        if (isLive()) {
          const live = currentMarksLive
          const refreshed = currentRefresh
          commit((previous) => ({
            ...previous,
            loading: false,
            ...(live
              ? { connection: 'disconnected' as const, syncError: toMessage(caught) }
              : refreshed
                ? { syncError: REFRESH_FAILURE_MESSAGE }
                : { error: toMessage(caught) }),
          }))
        }
      } finally {
        state.inFlight = false
        const waiters = state.waiters.splice(0)
        waiters.forEach((resolve) => resolve(succeeded))
      }
    },
    [commit, owner],
  )

  useEffect(() => {
    if (!runId) {
      return
    }

    const generation = ++generationRef.current
    const catchUpState: CatchUpState = {
      cursor: 0,
      inFlight: false,
      pending: false,
      pendingMarksLive: false,
      pendingRefresh: false,
      waiters: [],
    }
    activeRef.current = { owner, runId, generation, state: catchUpState }
    // A fresh lifetime already starts from the loading frame; a second effect run of the same
    // lifetime (React Strict Mode) restarts the cursor, so the timeline restarts with it.
    commit((previous) => ({
      ...previous,
      cards: [],
      connection: 'connecting',
      loading: previous.cockpit === null,
      error: null,
      syncError: null,
    }))

    void fetchCatchUp(runId, generation, catchUpState, false)

    const hubConnection = createRunNotificationConnection()

    hubConnection.on('runAdvanced', (notification: RunAdvancedNotification) => {
      if (
        generationRef.current === generation &&
        notification.runId === runId &&
        notification.latestSequence > catchUpState.cursor
      ) {
        void fetchCatchUp(runId, generation, catchUpState, true)
      }
    })
    hubConnection.onreconnecting(() => {
      if (generationRef.current === generation) {
        commit((previous) => ({ ...previous, connection: 'reconnecting' }))
      }
    })
    hubConnection.onreconnected(() => {
      if (generationRef.current === generation) {
        void fetchCatchUp(runId, generation, catchUpState, true)
      }
    })
    hubConnection.onclose(() => {
      if (generationRef.current === generation) {
        commit((previous) => ({ ...previous, connection: 'disconnected' }))
      }
    })

    hubConnection
      .start()
      .then(() => {
        // An event committed between the initial query above and the connection actually
        // coming up cannot have triggered a notification (there was no live transport yet
        // to deliver one), so an authoritative catch-up runs once more here. `fetchCatchUp`
        // itself is what moves `connection` to 'live', and only once this actually
        // succeeds — closing the gap without ever claiming synchronization that didn't
        // happen.
        if (generationRef.current === generation) {
          void fetchCatchUp(runId, generation, catchUpState, true)
        }
      })
      .catch(() => {
        if (generationRef.current === generation) {
          commit((previous) => ({ ...previous, connection: 'disconnected' }))
        }
      })

    return () => {
      // Invalidates this generation immediately, including on a plain unmount where no
      // newer effect run ever bumps it: any callback or async completion still pending
      // from this connection (onclose firing as a result of stop(), a fetch already in
      // flight, a queued coalesced pass) can no longer change state for a run that is no
      // longer current.
      generationRef.current += 1
      if (activeRef.current?.generation === generation) {
        activeRef.current = null
      }
      // Anyone still awaiting a refresh for this now-invalid generation is released with false.
      catchUpState.waiters.splice(0).forEach((resolve) => resolve(false))
      void hubConnection.stop()
    }
  }, [runId, owner, commit, fetchCatchUp])

  // Bound to this selection's lifetime: a callback retained from an earlier selection (a save
  // continuation of the previous run) can neither refresh nor wait on the current one.
  const refresh = useCallback(() => {
    const active = activeRef.current
    if (!active || active.owner !== owner || generationRef.current !== active.generation) {
      return Promise.resolve(false)
    }

    return new Promise<boolean>((resolve) => {
      void fetchCatchUp(active.runId, active.generation, active.state, false, true, resolve)
    })
  }, [fetchCatchUp, owner])

  return { ...frame, refresh }
}
