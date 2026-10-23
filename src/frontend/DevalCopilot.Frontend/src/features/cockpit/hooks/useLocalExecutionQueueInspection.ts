import { useCallback, useEffect } from 'react'
import { localExecutionQueueInspectionClient } from '../../../api/clients'
import { isSameQueueInspectionEntry, toLocalExecutionQueueInspection } from '../localExecutionQueueInspection'
import type { LocalExecutionQueueInspection, LocalExecutionQueueInspectionEntry } from '../localExecutionQueueInspection'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'

/** The one overview request is aborted after this long: a hung request can never keep a stale answer or a pending state current. */
export const LOCAL_EXECUTION_QUEUE_INSPECTION_TIMEOUT_MS = 10_000

/** `read`: the request failed. `timeout`: it did not answer in time. `invalid`: the answer is not coherent. */
export type LocalExecutionQueueInspectionFailure = 'read' | 'timeout' | 'invalid'

/** The single owner key of the authenticated host session's lifetime: independent of any project or run selection. */
const HOST_SESSION = 'host-session'

export interface SelectedQueueInspectionEntry {
  /** The lifetime of this selection: another selection, even of the same entry, has another key. */
  key: string
  /** The entry of this visit's validated overview that was selected, never one supplied by the caller. */
  entry: LocalExecutionQueueInspectionEntry
}

export interface UseLocalExecutionQueueInspectionResult {
  /** Whether the global region is open. */
  open: boolean
  /** The lifetime of the current overview visit (null while closed). */
  visitKey: string | null
  pending: boolean
  /** The validated overview of this visit; never together with a failure. */
  overview: LocalExecutionQueueInspection | null
  failure: LocalExecutionQueueInspectionFailure | null
  /** The recorded cycle selected in this visit, whose existing recorded inspector the region renders. */
  selection: SelectedQueueInspectionEntry | null
  /** Opens the region and reads the overview once; an open region is left as it is. */
  show: () => void
  /** Closes the opening that created this callback, discarding its overview, its selection and any read in flight. */
  close: () => void
  /** Reads the overview again as a new visit: the earlier visit, its rows and its selection end before the read starts. */
  reload: () => void
  /**
   * Selects one recorded cycle of THIS visit. A callback of an ended visit, or an entry the visit's validated overview does not hold
   * exactly (admission, sequence, run and project), does nothing: nothing is searched for or selected in its place, and nothing is read.
   */
  select: (entry: LocalExecutionQueueInspectionEntry) => void
  /** Ends the selection this callback was created for; a retained callback of an earlier selection does nothing. */
  clearSelection: () => void
}

interface OpeningFrame {
  open: boolean
  serial: number
  visit: number
}

interface VisitFrame {
  pending: boolean
  overview: LocalExecutionQueueInspection | null
  failure: LocalExecutionQueueInspectionFailure | null
  selected: { entry: LocalExecutionQueueInspectionEntry; serial: number } | null
  selections: number
}

const createClosed = (): OpeningFrame => ({ open: false, serial: 0, visit: 0 })
const createPendingVisit = (): VisitFrame => ({ pending: true, overview: null, failure: null, selected: null, selections: 0 })

/**
 * The explicit recorded inspection of every outstanding queue admission (ADR-0064) and, as a separate explicit action, the existing
 * recorded inspector of one selected cycle. Lifetimes nest and each owns its callbacks and results: the authenticated host session, each
 * opening, each overview visit (an open or a reload) and each selection. A replacement derives its first frame in the same render, so
 * closing, reopening, a reload, another selection, a return to an earlier one (A-B-A) or unmounting can never show or settle anything of
 * an earlier lifetime, and a retained callback of an ended lifetime reads and selects nothing. Selecting rechecks the exact entry in the
 * visit's validated overview when it runs.
 *
 * Nothing is read until the owner opens or reloads the region, and no project selection owns, refreshes or restarts it. Nothing is
 * polled, retried automatically, prefetched, cached across lifetimes, logged, stored or exported, and nothing here sends a command,
 * reads an operation or an artifact, or touches the capacity or live control readers. The one GET carries a real AbortSignal: ending its
 * visit or its ten-second bound aborts the request itself, not only its continuation.
 */
export function useLocalExecutionQueueInspection(): UseLocalExecutionQueueInspectionResult {
  const sessionOwner = useOwnedLifetime(HOST_SESSION)
  const [opening, commitOpening] = useOwnedState(sessionOwner, createClosed)
  const isOpen = opening.open
  const visitKey = isOpen ? JSON.stringify([opening.serial, opening.visit]) : null
  const visitOwner = useOwnedLifetime(visitKey)
  const [frame, commitFrame] = useOwnedState(visitOwner, createPendingVisit)
  const selected = isOpen ? frame.selected : null
  const selectionKey =
    selected === null
      ? null
      : JSON.stringify([
          visitKey,
          selected.serial,
          selected.entry.admission.admissionId,
          selected.entry.sequence.sequenceId,
          selected.entry.runId,
          selected.entry.projectId,
        ])
  const selectionOwner = useOwnedLifetime(selectionKey)

  const show = useCallback(() => {
    if (!sessionOwner.isActive()) {
      return
    }

    commitOpening((previous) => (previous.open ? previous : { open: true, serial: previous.serial + 1, visit: 1 }))
  }, [sessionOwner, commitOpening])

  const { serial, visit } = opening
  const close = useCallback(() => {
    if (!visitOwner.isActive() || visitOwner.key === null) {
      return
    }

    commitOpening((previous) => (previous.serial === serial && previous.open ? { ...previous, open: false } : previous))
  }, [visitOwner, serial, commitOpening])

  const reload = useCallback(() => {
    if (!visitOwner.isActive() || visitOwner.key === null) {
      return
    }

    // Decided against the queued value: only the visit this callback was rendered for is replaced, once.
    commitOpening((previous) =>
      previous.open && previous.serial === serial && previous.visit === visit ? { ...previous, visit: previous.visit + 1 } : previous,
    )
  }, [visitOwner, serial, visit, commitOpening])

  useEffect(() => {
    if (visitOwner.key === null) {
      return undefined
    }

    const isCurrent = visitOwner.begin('overview')
    const controller = new AbortController()
    let finished = false
    let timer: number | undefined
    const settle = (settled: Pick<VisitFrame, 'pending' | 'overview' | 'failure'>) => {
      if (finished) {
        return
      }

      finished = true
      window.clearTimeout(timer)
      if (isCurrent()) {
        commitFrame((previous) => ({ ...previous, ...settled }))
      }
    }

    timer = window.setTimeout(() => {
      settle({ pending: false, overview: null, failure: 'timeout' })
      controller.abort()
    }, LOCAL_EXECUTION_QUEUE_INSPECTION_TIMEOUT_MS)

    let request: Promise<unknown>
    try {
      request = localExecutionQueueInspectionClient(controller.signal).readOutstanding()
    } catch (caught) {
      request = Promise.reject(caught)
    }

    request.then(
      (answer) => {
        const accepted = toLocalExecutionQueueInspection(answer)
        settle(
          accepted === null
            ? { pending: false, overview: null, failure: 'invalid' }
            : { pending: false, overview: accepted, failure: null },
        )
      },
      () => settle({ pending: false, overview: null, failure: 'read' }),
    )

    return () => {
      finished = true
      window.clearTimeout(timer)
      controller.abort()
    }
  }, [visitOwner, commitFrame])

  const select = useCallback(
    (entry: LocalExecutionQueueInspectionEntry) => {
      if (!visitOwner.isActive() || visitOwner.key === null) {
        return
      }

      commitFrame((previous) => {
        const held = previous.overview?.status === 'Recorded' ? previous.overview.entries.find((candidate) => isSameQueueInspectionEntry(candidate, entry)) : undefined
        return held === undefined ? previous : { ...previous, selected: { entry: held, serial: previous.selections + 1 }, selections: previous.selections + 1 }
      })
    },
    [visitOwner, commitFrame],
  )

  const selectedSerial = selected?.serial ?? null
  const clearSelection = useCallback(() => {
    if (!selectionOwner.isActive() || selectionOwner.key === null) {
      return
    }

    commitFrame((previous) => (previous.selected !== null && previous.selected.serial === selectedSerial ? { ...previous, selected: null } : previous))
  }, [selectionOwner, selectedSerial, commitFrame])

  return {
    open: isOpen,
    visitKey,
    pending: isOpen && frame.pending,
    overview: isOpen ? frame.overview : null,
    failure: isOpen ? frame.failure : null,
    selection: selected !== null && selectionKey !== null ? { key: selectionKey, entry: selected.entry } : null,
    show,
    close,
    reload,
    select,
    clearSelection,
  }
}
