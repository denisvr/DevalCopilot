import { useCallback, useEffect, useLayoutEffect, useRef } from 'react'
import type { AgentAttemptHistoryEntryResponse } from '../../../api/clients'
import { agentAttemptHistoryClient } from '../../../api/clients'
import { AGENT_ATTEMPT_HISTORY_PAGE_SIZE, toAgentAttemptHistoryPage } from '../agentAttemptHistory'
import type { AgentAttemptHistoryRequest } from '../agentAttemptHistory'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'
import type { OwnedLifetime } from './useOwnedLifetime'

/** `read`: a request failed. `invalid`: the host answered, but not with a coherent page for exactly this request. */
export type AgentAttemptHistoryFailure = 'read' | 'invalid'
export type AgentAttemptHistoryPending = 'first' | 'older'

export interface UseAgentAttemptHistoryResult {
  /** Whether the history is expanded; false (collapsed) for every newly committed run. */
  open: boolean
  /** Expands (one first-page read) or collapses (every row, the selection and any pending read are discarded). */
  setOpen: (next: boolean) => void
  /** The rows this open lifetime accepted, newest first; only validated pages ever contribute. */
  rows: readonly AgentAttemptHistoryEntryResponse[]
  pending: AgentAttemptHistoryPending | null
  failure: AgentAttemptHistoryFailure | null
  /** Which page the failure belongs to. */
  failedPage: AgentAttemptHistoryPending | null
  /** An older page is known to exist and none is in flight or failed. */
  hasMore: boolean
  /** Reads the next older page with the host's exact cursor. */
  loadOlder: () => void
  /** Reads the failed page again: the first page again, or the same older cursor, keeping only the valid rows of this lifetime. */
  retry: () => void
  /** The selected verified row of this open lifetime, or null. */
  selected: AgentAttemptHistoryEntryResponse | null
  /** The key of the committed selection's lifetime (null when none): what the evidence and artifact reads of the selection belong to. */
  selectionKey: string | null
  /** Selects a loaded verified row; each committed selection (including a return to an earlier row) is a new selection lifetime. */
  select: (attemptId: string) => void
  /** Closes exactly the committed selection it was created for; the callback of an earlier selection starts and closes nothing. */
  closeSelected: () => void
}

// What the committed run owns: whether its attempts are expanded, and how many times they were opened, so each opening is a lifetime.
interface RunFrame {
  open: boolean
  generation: number
}

// What one open lifetime owns: its validated rows, the cursor of the next older page, its one pending read, a failure and the selection.
interface AttemptFrame {
  rows: readonly AgentAttemptHistoryEntryResponse[]
  nextBefore: number | null
  pending: AgentAttemptHistoryPending | null
  failure: AgentAttemptHistoryFailure | null
  failedPage: AgentAttemptHistoryPending | null
  selectedAttemptId: string | null
  // How many selections this open lifetime committed, so choosing A, then B, then A again yields three distinct selection lifetimes.
  selectionEpoch: number
}

const createRunFrame = (): RunFrame => ({ open: false, generation: 0 })

const createAttemptFrame = (): AttemptFrame => ({
  rows: [],
  nextBefore: null,
  pending: 'first',
  failure: null,
  failedPage: null,
  selectedAttemptId: null,
  selectionEpoch: 0,
})

/**
 * The paged, read-only Agent-attempt history of one run (the current run in the usage rail, or a selected historical run). Three
 * lifetimes own it: the committed run (collapsed by default; another run, a return to an earlier one or unmounting ends it), each opening
 * of that run (a fresh first-page read with no rows and no selection; closing and reopening end the previous one) and, inside it, the one
 * selected attempt. A replacement derives its own empty first frame in the same render, an older or replaced read can neither overwrite
 * nor settle a newer one, and the callbacks of an ended lifetime start nothing. A page is accepted only whole and only when it is
 * coherent for exactly its request; otherwise it is an explicit failure that keeps just the valid rows of its own lifetime, and the same
 * cursor is read again only on request. Nothing is polled, cached across lifetimes, retried automatically or written to browser storage.
 */
export function useAgentAttemptHistory(runId: string | null): UseAgentAttemptHistoryResult {
  const runOwner = useOwnedLifetime(runId)
  const [run, commitRun] = useOwnedState(runOwner, createRunFrame)
  const open = runId !== null && run.open
  const owner = useOwnedLifetime(open ? JSON.stringify([runId, run.generation]) : null)
  const [frame, commit] = useOwnedState(owner, createAttemptFrame)

  // The frame this render committed and the one read in flight, both keyed by their lifetime, so a callback retained from an earlier
  // lifetime can never act on a later one and two calls in the same tick send one request.
  const displayed = useRef<{ owner: OwnedLifetime; frame: AttemptFrame } | null>(null)
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

  const read = useCallback(
    (page: AgentAttemptHistoryPending, before: number | null, retained: readonly AgentAttemptHistoryEntryResponse[]) => {
      if (runId === null) {
        return
      }

      const isCurrent = owner.begin('page')
      inFlight.current = { owner, isCurrent }
      const request: AgentAttemptHistoryRequest = { runId, beforeAttemptNumber: before, limit: AGENT_ATTEMPT_HISTORY_PAGE_SIZE }
      const settle = (update: (previous: AttemptFrame) => AttemptFrame) => {
        if (isCurrent()) {
          inFlight.current = null
          commit(update)
        }
      }

      commit((previous) => ({ ...previous, pending: page, failure: null, failedPage: null }))
      agentAttemptHistoryClient()
        .getAgentAttemptHistory(runId, before, AGENT_ATTEMPT_HISTORY_PAGE_SIZE)
        .then(
          (response) => {
            const accepted = toAgentAttemptHistoryPage(response, request, retained)
            settle((previous) => accepted === null
              ? { ...previous, pending: null, failure: 'invalid', failedPage: page }
              : {
                ...previous,
                rows: [...retained, ...accepted.entries],
                nextBefore: accepted.nextBeforeAttemptNumber,
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

  // The displayed frame of exactly this open lifetime, when it may start a read now (nothing of it in flight).
  const idleFrame = useCallback((): AttemptFrame | null => {
    const current = displayed.current
    if (!owner.isActive() || owner.key === null || current === null || current.owner !== owner) {
      return null
    }

    return current.frame.pending !== null || (inFlight.current?.owner === owner && inFlight.current.isCurrent()) ? null : current.frame
  }, [owner])

  const loadOlder = useCallback(() => {
    const current = idleFrame()
    if (current !== null && current.failure === null && current.nextBefore !== null) {
      read('older', current.nextBefore, current.rows)
    }
  }, [idleFrame, read])

  const retry = useCallback(() => {
    const current = idleFrame()
    if (current?.failedPage === 'first') {
      read('first', null, [])
    } else if (current?.failedPage === 'older' && current.nextBefore !== null) {
      read('older', current.nextBefore, current.rows)
    }
  }, [idleFrame, read])

  const select = useCallback(
    (attemptId: string) => {
      if (!owner.isActive() || owner.key === null) {
        return
      }

      // Choosing the attempt that is already selected keeps its lifetime; any other loaded, verified row is a new committed selection.
      commit((previous) => previous.selectedAttemptId === attemptId
        || !previous.rows.some((row) => row.attemptId === attemptId && row.identityValid === true)
        ? previous
        : { ...previous, selectedAttemptId: attemptId, selectionEpoch: previous.selectionEpoch + 1 })
    },
    [owner, commit],
  )

  // The committed selection owns its reads and actions. Its key names the open lifetime, the attempt and the selection count, so another
  // attempt, a return to an earlier one, a close, another opening, another run or unmounting each end it.
  const selectedAttemptId = open ? frame.selectedAttemptId : null
  const selectionEpoch = frame.selectionEpoch
  const selection = useOwnedLifetime(selectedAttemptId === null ? null : JSON.stringify([owner.key, selectedAttemptId, selectionEpoch]))

  const closeSelected = useCallback(() => {
    if (!selection.isActive() || selection.key === null) {
      return
    }

    commit((previous) => previous.selectedAttemptId === selectedAttemptId && previous.selectionEpoch === selectionEpoch
      ? { ...previous, selectedAttemptId: null }
      : previous)
  }, [selection, commit, selectedAttemptId, selectionEpoch])

  const rows = open ? frame.rows : []
  return {
    open,
    setOpen,
    rows,
    pending: open ? frame.pending : null,
    failure: open ? frame.failure : null,
    failedPage: open ? frame.failedPage : null,
    hasMore: open && frame.nextBefore !== null && frame.pending === null && frame.failure === null,
    loadOlder,
    retry,
    selected: rows.find((row) => row.attemptId === selectedAttemptId) ?? null,
    selectionKey: selection.key,
    select,
    closeSelected,
  }
}
