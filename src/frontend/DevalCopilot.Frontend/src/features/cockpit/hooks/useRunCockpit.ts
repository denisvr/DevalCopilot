import { useCallback, useEffect, useRef, useState } from 'react'
import type { GetRunCockpitResponse, RunEventResponse } from '../../../api/clients'
import { runCockpitClient, runEventsClient } from '../../../api/clients'
import { createRunNotificationConnection, type RunAdvancedNotification } from '../../../api/runNotifications'
import type { CollaborationCard, ConnectionState } from '../types'
import { parseEventSummary } from '../parseEventSummary'
import { toParticipantIdentity } from '../participantIdentity'

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
}

function toCard(event: RunEventResponse): CollaborationCard {
  return {
    sequence: event.sequence ?? 0,
    id: event.id ?? '',
    attemptId: event.attemptId ?? null,
    eventType: event.eventType ?? '',
    actor: toParticipantIdentity(event.actor),
    summary: parseEventSummary(event.payloadJson ?? ''),
    occurredAtUtc: (event.occurredAtUtc as unknown as string) ?? '',
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
 */
export function useRunCockpit(runId: string | null): UseRunCockpitResult {
  const [cockpit, setCockpit] = useState<GetRunCockpitResponse | null>(null)
  const [cards, setCards] = useState<CollaborationCard[]>([])
  const [connection, setConnection] = useState<ConnectionState>('connecting')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [syncError, setSyncError] = useState<string | null>(null)
  // Identifies the current effect run. Bumped both when a newer run starts AND in this
  // effect's own cleanup (see below), so a plain unmount — where no newer generation is
  // ever created — still invalidates the generation immediately, rather than leaving it
  // matching forever.
  const generationRef = useRef(0)

  // A single run of this loop can serve several coalesced requests (the pre-connect load,
  // then a post-connect/reconnect/notification refresh queued while it was still in
  // flight). `marksLive` — and therefore whether a failure is fatal (`error`) or transient
  // (`syncError` + not-live) — is judged per PASS actually executed, tracked via
  // `currentMarksLive`/`state.pendingMarksLive`, never by whichever caller happened to
  // start the loop. All classification and state updates happen inside this function; it
  // never rejects, so no caller needs its own `.catch()`.
  const fetchCatchUp = useCallback(
    async (currentRunId: string, generation: number, state: CatchUpState, marksLive: boolean) => {
      if (state.inFlight) {
        state.pending = true
        state.pendingMarksLive = state.pendingMarksLive || marksLive
        return
      }

      state.inFlight = true
      let currentMarksLive = marksLive
      try {
        do {
          state.pending = false
          state.pendingMarksLive = false

          const [nextCockpit, nextEvents] = await Promise.all([
            runCockpitClient().getRunCockpit(currentRunId),
            runEventsClient().getRunEvents(currentRunId, state.cursor),
          ])

          if (generationRef.current !== generation) {
            return
          }

          setLoading(false)
          setCockpit(nextCockpit)
          if (nextEvents.length > 0) {
            state.cursor = Math.max(state.cursor, ...nextEvents.map((event) => event.sequence ?? 0))
            setCards((previous) => mergeBySequence(previous, nextEvents))
          }

          if (currentMarksLive) {
            setConnection('live')
            setSyncError(null)
          } else {
            setError(null)
          }

          currentMarksLive = state.pendingMarksLive
        } while (state.pending && generationRef.current === generation)
      } catch (caught) {
        if (generationRef.current === generation) {
          setLoading(false)
          if (currentMarksLive) {
            setConnection('disconnected')
            setSyncError(toMessage(caught))
          } else {
            setError(toMessage(caught))
          }
        }
      } finally {
        state.inFlight = false
      }
    },
    [],
  )

  useEffect(() => {
    if (!runId) {
      return
    }

    const generation = ++generationRef.current
    const catchUpState: CatchUpState = { cursor: 0, inFlight: false, pending: false, pendingMarksLive: false }
    setLoading(true)
    setError(null)
    setSyncError(null)
    setCards([])
    setConnection('connecting')

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
        setConnection('reconnecting')
      }
    })
    hubConnection.onreconnected(() => {
      if (generationRef.current === generation) {
        void fetchCatchUp(runId, generation, catchUpState, true)
      }
    })
    hubConnection.onclose(() => {
      if (generationRef.current === generation) {
        setConnection('disconnected')
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
          setConnection('disconnected')
        }
      })

    return () => {
      // Invalidates this generation immediately, including on a plain unmount where no
      // newer effect run ever bumps it: any callback or async completion still pending
      // from this connection (onclose firing as a result of stop(), a fetch already in
      // flight, a queued coalesced pass) can no longer change state for a run that is no
      // longer current.
      generationRef.current += 1
      void hubConnection.stop()
    }
  }, [runId, fetchCatchUp])

  return { cockpit, cards, connection, loading, error, syncError }
}
