import { useCallback, useEffect, useLayoutEffect, useRef } from 'react'
import { runEventHistoryClient } from '../../../api/clients'
import { RUN_EVENT_HISTORY_PAGE_SIZE, toRunEventHistoryPage } from '../runEventHistory'
import type { RunEventHeader, RunEventHistoryRequest } from '../runEventHistory'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'
import type { OwnedLifetime } from './useOwnedLifetime'

/** `read`: a request failed. `invalid`: the host answered, but not with a coherent page for exactly this request. */
export type RunEventHistoryFailure = 'read' | 'invalid'
export type RunEventHistoryPending = 'first' | 'older'

export interface UseRunEventHistoryResult {
  /** Whether the recorded-events region is expanded; false (collapsed) for every newly committed run. */
  open: boolean
  /** Expands (one newest-page read) or collapses (every row, error and pending read are discarded). */
  setOpen: (next: boolean) => void
  /** The headers this open lifetime accepted, newest first; only validated pages ever contribute. */
  rows: readonly RunEventHeader[]
  pending: RunEventHistoryPending | null
  failure: RunEventHistoryFailure | null
  /** Which page the failure belongs to: a failed first page has no rows and is read again only by `reload`. */
  failedPage: RunEventHistoryPending | null
  /** An older page is known to exist and none is in flight. */
  hasMore: boolean
  /** Reads the next older page with the host's exact cursor, or the same cursor again after a failed older page. */
  loadOlder: () => void
  /** Starts a fresh open lifetime: a new newest-page read with no rows and no error. */
  reload: () => void
}

// What the committed run owns: whether its events are expanded, and how many times they were opened or reloaded, so each is a lifetime.
interface RunFrame {
  open: boolean
  generation: number
}

// What one open lifetime owns: its validated rows, the exact cursor of the next older page, its one pending read and a failure.
interface EventFrame {
  rows: readonly RunEventHeader[]
  nextBefore: string | null
  pending: RunEventHistoryPending | null
  failure: RunEventHistoryFailure | null
  failedPage: RunEventHistoryPending | null
}

const createRunFrame = (): RunFrame => ({ open: false, generation: 0 })

const createEventFrame = (): EventFrame => ({ rows: [], nextBefore: null, pending: 'first', failure: null, failedPage: null })

/**
 * The paged, read-only journal headers of one recorded run. Two lifetimes own it: the committed run (collapsed by default; another
 * run, a return to an earlier one or unmounting ends it) and each open or reload of that run (a fresh newest-page read with no rows;
 * closing, reopening and reloading end the previous one). A replacement derives its own empty first frame in the same render, an
 * older or replaced read can neither overwrite nor settle a newer one, and the callbacks of an ended lifetime start nothing. A page
 * is accepted only whole and only when it is coherent for exactly its request; otherwise it is an explicit failure that keeps just
 * the valid rows of its own lifetime, and an older page is retried with the same cursor. Nothing is polled, cached across lifetimes
 * or retried automatically, and a sequence is only ever handled as the exact text the host sent.
 */
export function useRunEventHistory(runId: string | null): UseRunEventHistoryResult {
  const runOwner = useOwnedLifetime(runId)
  const [run, commitRun] = useOwnedState(runOwner, createRunFrame)
  const open = runId !== null && run.open
  const generation = run.generation
  const owner = useOwnedLifetime(open ? JSON.stringify([runId, generation]) : null)
  const [frame, commit] = useOwnedState(owner, createEventFrame)

  // The frame this render committed and the one read in flight, both keyed by their lifetime, so a callback retained from an
  // earlier lifetime can never act on a later one and two calls in the same tick send one request.
  const displayed = useRef<{ owner: OwnedLifetime; frame: EventFrame } | null>(null)
  const inFlight = useRef<{ owner: OwnedLifetime; isCurrent: () => boolean } | null>(null)

  useLayoutEffect(() => {
    displayed.current = { owner, frame }
  })

  const setOpen = useCallback(
    (next: boolean) => {
      if (!runOwner.isActive() || runOwner.key === null) {
        return
      }

      commitRun((previous) => (previous.open === next ? previous : { open: next, generation: next ? previous.generation + 1 : previous.generation }))
    },
    [runOwner, commitRun],
  )

  const reload = useCallback(() => {
    if (!owner.isActive() || owner.key === null) {
      return
    }

    commitRun((previous) => (previous.open && previous.generation === generation ? { ...previous, generation: previous.generation + 1 } : previous))
  }, [owner, commitRun, generation])

  const read = useCallback(
    (page: RunEventHistoryPending, before: string | null, retained: readonly RunEventHeader[]) => {
      if (runId === null) {
        return
      }

      const isCurrent = owner.begin('page')
      inFlight.current = { owner, isCurrent }
      const request: RunEventHistoryRequest = { runId, beforeSequence: before, limit: RUN_EVENT_HISTORY_PAGE_SIZE }
      const settle = (update: (previous: EventFrame) => EventFrame) => {
        if (isCurrent()) {
          inFlight.current = null
          commit(update)
        }
      }

      if (page === 'older') {
        commit((previous) => ({ ...previous, pending: 'older', failure: null, failedPage: null }))
      }

      runEventHistoryClient()
        .getRunEventHistory(runId, before, RUN_EVENT_HISTORY_PAGE_SIZE)
        .then(
          (response) => {
            const accepted = toRunEventHistoryPage(response, request, retained)
            settle((previous) => accepted === null
              ? { ...previous, pending: null, failure: 'invalid', failedPage: page }
              : {
                rows: [...retained, ...accepted.entries],
                nextBefore: accepted.nextBeforeSequence,
                pending: null,
                failure: null,
                failedPage: null,
              })
          },
          () => settle((previous) => ({ ...previous, pending: null, failure: 'read', failedPage: page })),
        )
    },
    [owner, commit, runId],
  )

  useEffect(() => {
    if (owner.key !== null) {
      read('first', null, [])
    }
  }, [owner, read])

  const loadOlder = useCallback(() => {
    const current = displayed.current
    if (!owner.isActive() || owner.key === null || current === null || current.owner !== owner) {
      return
    }

    const { nextBefore, pending, rows } = current.frame
    if (nextBefore === null || pending !== null || (inFlight.current?.owner === owner && inFlight.current.isCurrent())) {
      return
    }

    read('older', nextBefore, rows)
  }, [owner, read])

  return {
    open,
    setOpen,
    rows: open ? frame.rows : [],
    pending: open ? frame.pending : null,
    failure: open ? frame.failure : null,
    failedPage: open ? frame.failedPage : null,
    hasMore: open && frame.nextBefore !== null && frame.pending === null,
    loadOlder,
    reload,
  }
}
