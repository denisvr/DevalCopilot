import { useCallback, useEffect, useLayoutEffect, useRef } from 'react'
import { localExecutionAdmissionHistoryClient } from '../../../api/clients'
import { toLocalExecutionAdmissionHistory } from '../localExecutionAdmissionHistory'
import type { LocalExecutionAdmissionHistory } from '../localExecutionAdmissionHistory'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'
import type { OwnedLifetime } from './useOwnedLifetime'

/** `read`: the request failed. `not-found`: the host no longer knows this run. `invalid`: the answer is not coherent for the request. */
export type LocalExecutionAdmissionHistoryFailure = 'read' | 'not-found' | 'invalid'

export interface UseLocalExecutionAdmissionHistoryResult {
  /** Whether the ledger region of the current run is open. */
  open: boolean
  /** The lifetime of the current ledger visit (null while closed). */
  visitKey: string | null
  pending: boolean
  /** The validated ledger of this visit; never together with a failure. */
  history: LocalExecutionAdmissionHistory | null
  failure: LocalExecutionAdmissionHistoryFailure | null
  /** Opens the region and reads the ledger once; an open region is left as it is. */
  show: () => void
  /** Closes the opening that created this callback, discarding its ledger and any read in flight. */
  close: () => void
  /** Reads the ledger again as a new visit: the earlier visit ends before the read starts. */
  reload: () => void
  /**
   * Opens the existing selected-step evidence for the step one recorded admission of THIS visit links. A callback of an ended visit, a
   * step the visit's validated ledger does not link, or a step the inspector's own accepted index lacks does nothing: nothing is
   * searched for, selected in its place or read for another visit.
   */
  inspectLinkedStep: (stepId: string) => void
}

interface OpeningFrame {
  open: boolean
  serial: number
  visit: number
}

interface LedgerFrame {
  pending: boolean
  history: LocalExecutionAdmissionHistory | null
  failure: LocalExecutionAdmissionHistoryFailure | null
}

const createClosed = (): OpeningFrame => ({ open: false, serial: 0, visit: 0 })
const createPendingLedger = (): LedgerFrame => ({ pending: true, history: null, failure: null })

const isNotFound = (caught: unknown): boolean =>
  typeof caught === 'object' && caught !== null && (caught as { status?: unknown }).status === 404

/**
 * The explicit recorded queue-admission inspection of one run (ADR-0063): the complete admission ledger of its queued local execution
 * cycle and, as a separate explicit action, the existing selected-step evidence of a step an admission links. Lifetimes nest and each
 * owns its callbacks and results: the run, each opening and each ledger visit (an open or a reload). A replacement derives its first
 * frame in the same render, so another run, a return to an earlier one (A-B-A), closing, reopening, a reload or unmounting can never show
 * or settle anything of an earlier lifetime, and a retained callback of an ended lifetime reads, selects and opens nothing.
 *
 * Nothing is read until the owner opens the region. Nothing is polled, retried automatically, prefetched, cached across lifetimes,
 * logged, stored or exported, and nothing here sends a command, reads an operation or an artifact, or touches the run's live control
 * reader. `selectStep` is the inspector's own selection and re-judges the current accepted index when it runs.
 */
export function useLocalExecutionAdmissionHistory(
  runId: string | null,
  selectStep: (stepId: string) => void,
): UseLocalExecutionAdmissionHistoryResult {
  const runOwner = useOwnedLifetime(runId)
  const [opening, commitOpening] = useOwnedState(runOwner, createClosed)
  const isOpen = runId !== null && opening.open
  const visitKey = isOpen ? JSON.stringify([runId, opening.serial, opening.visit]) : null
  const visitOwner = useOwnedLifetime(visitKey)
  const [frame, commitFrame] = useOwnedState(visitOwner, createPendingLedger)

  const displayed = useRef<{ owner: OwnedLifetime; frame: LedgerFrame } | null>(null)
  useLayoutEffect(() => {
    displayed.current = { owner: visitOwner, frame }
  })

  const show = useCallback(() => {
    if (!runOwner.isActive() || runOwner.key === null) {
      return
    }

    commitOpening((previous) => (previous.open ? previous : { open: true, serial: previous.serial + 1, visit: 1 }))
  }, [runOwner, commitOpening])

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
    if (visitOwner.key === null || runId === null) {
      return
    }

    const isCurrent = visitOwner.begin('ledger')
    const settle = (settled: LedgerFrame) => {
      if (isCurrent()) {
        commitFrame(() => settled)
      }
    }

    commitFrame(createPendingLedger)
    localExecutionAdmissionHistoryClient()
      .readAdmissions(runId)
      .then(
        (answer) => {
          const accepted = toLocalExecutionAdmissionHistory(answer, runId)
          settle(accepted === null ? { pending: false, history: null, failure: 'invalid' } : { pending: false, history: accepted, failure: null })
        },
        (caught: unknown) => settle({ pending: false, history: null, failure: isNotFound(caught) ? 'not-found' : 'read' }),
      )
  }, [visitOwner, runId, commitFrame])

  const inspectLinkedStep = useCallback(
    (stepId: string) => {
      const current = displayed.current
      if (!visitOwner.isActive() || visitOwner.key === null || current === null || current.owner !== visitOwner) {
        return
      }

      const ledger = current.frame.history
      if (ledger?.status !== 'Recorded' || !ledger.admissions.some((admission) => admission.stepId === stepId)) {
        return
      }

      selectStep(stepId)
    },
    [visitOwner, selectStep],
  )

  return {
    open: isOpen,
    visitKey,
    pending: isOpen && frame.pending,
    history: isOpen ? frame.history : null,
    failure: isOpen ? frame.failure : null,
    show,
    close,
    reload,
    inspectLinkedStep,
  }
}
