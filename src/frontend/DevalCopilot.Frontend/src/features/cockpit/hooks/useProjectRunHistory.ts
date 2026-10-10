import { useCallback, useEffect, useLayoutEffect, useRef } from 'react'
import { projectRunHistoryClient } from '../../../api/clients'
import { RUN_HISTORY_PAGE_SIZE, toRunHistoryPage } from '../projectRunHistory'
import type { RunHistoryEntry, RunHistoryRequest } from '../projectRunHistory'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'
import type { OwnedLifetime } from './useOwnedLifetime'

/** `read`: a request failed. `invalid`: the host answered, but not with a coherent page for exactly this request. */
export type RunHistoryFailure = 'read' | 'invalid'
export type RunHistoryPending = 'first' | 'older'

export interface UseProjectRunHistoryResult {
  /** Whether the history region is expanded; false (collapsed) for every newly committed project. */
  open: boolean
  /** Expands (one first-page read) or collapses (every row, the selection and any pending read are discarded). */
  setOpen: (next: boolean) => void
  /** The rows this open lifetime accepted, newest first; only validated pages ever contribute. */
  rows: readonly RunHistoryEntry[]
  pending: RunHistoryPending | null
  failure: RunHistoryFailure | null
  /** Which page the failure belongs to: a failed first page has no rows and is read again only by `reload`. */
  failedPage: RunHistoryPending | null
  /** An older page is known to exist and none is in flight. */
  hasMore: boolean
  /** Reads the next older page with the host's cursor, or the same cursor again after a failed older page. */
  loadOlder: () => void
  /** Starts a fresh open lifetime: new first-page read, no rows, no selection. */
  reload: () => void
  selectedRunId: string | null
  /** Selects a loaded row; each committed selection (including a return to an earlier row) is a new selected-row lifetime. */
  select: (runId: string | null) => void
  /** Closes exactly the committed selection it was created for; the callback of an earlier selection starts and closes nothing. */
  closeSelected: () => void
}

// What the committed project owns: whether its history is expanded, and how many times it was opened or reloaded, so each is a lifetime.
interface ProjectFrame {
  open: boolean
  generation: number
}

// What one open lifetime owns: its validated rows, the cursor of the next older page, its one pending read, a failure and the selection.
interface HistoryFrame {
  rows: readonly RunHistoryEntry[]
  nextBefore: number | null
  pending: RunHistoryPending | null
  failure: RunHistoryFailure | null
  failedPage: RunHistoryPending | null
  selectedRunId: string | null
  // How many selections this open lifetime committed, so choosing A, then B, then A again yields three distinct selected-row lifetimes.
  selectionEpoch: number
}

const createProjectFrame = (): ProjectFrame => ({ open: false, generation: 0 })

const createHistoryFrame = (): HistoryFrame => ({
  rows: [],
  nextBefore: null,
  pending: 'first',
  failure: null,
  failedPage: null,
  selectedRunId: null,
  selectionEpoch: 0,
})

/**
 * The paged, read-only history of the selected project's recorded runs. Three lifetimes own it: the committed project (collapsed
 * by default; another project, a return to an earlier one or unmounting ends it), each open or reload of that project (a fresh
 * first-page read with no rows and no selection; closing, reopening and reloading end the previous one) and, inside it, the one
 * selected row. A replacement derives its own empty first frame in the same render, an older or replaced read can neither overwrite
 * nor settle a newer one, and the callbacks of an ended lifetime start nothing. A page is accepted only whole and only when it is
 * coherent for exactly its request; otherwise it is an explicit failure that keeps just the valid rows of its own lifetime, and an
 * older page is retried with the same cursor. Nothing is polled, cached across lifetimes or retried automatically.
 */
export function useProjectRunHistory(projectId: string | null): UseProjectRunHistoryResult {
  const projectOwner = useOwnedLifetime(projectId)
  const [project, commitProject] = useOwnedState(projectOwner, createProjectFrame)
  const open = projectId !== null && project.open
  const generation = project.generation
  const owner = useOwnedLifetime(open ? JSON.stringify([projectId, generation]) : null)
  const [frame, commit] = useOwnedState(owner, createHistoryFrame)

  // The frame this render committed and the one read in flight, both keyed by their lifetime, so a callback retained from an
  // earlier lifetime can never act on a later one and two calls in the same tick send one request.
  const displayed = useRef<{ owner: OwnedLifetime; frame: HistoryFrame } | null>(null)
  const inFlight = useRef<{ owner: OwnedLifetime; isCurrent: () => boolean } | null>(null)

  useLayoutEffect(() => {
    displayed.current = { owner, frame }
  })

  const setOpen = useCallback(
    (next: boolean) => {
      if (!projectOwner.isActive() || projectOwner.key === null) {
        return
      }

      commitProject((previous) => (previous.open === next ? previous : { open: next, generation: next ? previous.generation + 1 : previous.generation }))
    },
    [projectOwner, commitProject],
  )

  const reload = useCallback(() => {
    if (!owner.isActive() || owner.key === null) {
      return
    }

    commitProject((previous) => (previous.open && previous.generation === generation ? { ...previous, generation: previous.generation + 1 } : previous))
  }, [owner, commitProject, generation])

  const read = useCallback(
    (page: RunHistoryPending, before: number | null, retained: readonly RunHistoryEntry[]) => {
      if (projectId === null) {
        return
      }

      const isCurrent = owner.begin('page')
      inFlight.current = { owner, isCurrent }
      const request: RunHistoryRequest = { projectId, beforeExecutionNumber: before, limit: RUN_HISTORY_PAGE_SIZE }
      const settle = (update: (previous: HistoryFrame) => HistoryFrame) => {
        if (isCurrent()) {
          inFlight.current = null
          commit(update)
        }
      }

      if (page === 'older') {
        commit((previous) => ({ ...previous, pending: 'older', failure: null, failedPage: null }))
      }

      projectRunHistoryClient()
        .getProjectRunHistory(projectId, before, RUN_HISTORY_PAGE_SIZE)
        .then(
          (response) => {
            const accepted = toRunHistoryPage(response, request, retained)
            settle((previous) => accepted === null
              ? { ...previous, pending: null, failure: 'invalid', failedPage: page }
              : {
                ...previous,
                rows: [...retained, ...accepted.entries],
                nextBefore: accepted.nextBeforeExecutionNumber,
                pending: null,
                failure: null,
                failedPage: null,
              })
          },
          () => settle((previous) => ({ ...previous, pending: null, failure: 'read', failedPage: page })),
        )
    },
    [owner, commit, projectId],
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

  const select = useCallback(
    (runId: string | null) => {
      if (!owner.isActive() || owner.key === null) {
        return
      }

      // Choosing the run that is already selected keeps its lifetime; any other loaded row is a new committed selection.
      commit((previous) => runId === null
        ? previous.selectedRunId === null ? previous : { ...previous, selectedRunId: null }
        : previous.selectedRunId === runId || !previous.rows.some((row) => row.runId === runId)
          ? previous
          : { ...previous, selectedRunId: runId, selectionEpoch: previous.selectionEpoch + 1 })
    },
    [owner, commit],
  )

  // The committed selection owns its actions. Its key names the open lifetime, the row and the selection count, so another row, a
  // return to an earlier one (A to B to A), a close, a reload, another project or unmounting each end it and a callback of an ended
  // selection can no longer close the one that replaced it.
  const selectedRunId = open ? frame.selectedRunId : null
  const selectionEpoch = frame.selectionEpoch
  const selection = useOwnedLifetime(selectedRunId === null ? null : JSON.stringify([owner.key, selectedRunId, selectionEpoch]))

  const closeSelected = useCallback(() => {
    if (!selection.isActive() || selection.key === null) {
      return
    }

    commit((previous) => previous.selectedRunId === selectedRunId && previous.selectionEpoch === selectionEpoch
      ? { ...previous, selectedRunId: null }
      : previous)
  }, [selection, commit, selectedRunId, selectionEpoch])

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
    selectedRunId,
    select,
    closeSelected,
  }
}
