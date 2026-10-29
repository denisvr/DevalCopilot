import { useCallback, useEffect, useLayoutEffect, useRef } from 'react'
import { runAbandonmentInspectionClient } from '../../../api/clients'
import { toRunAbandonmentInspection } from '../runAbandonmentInspection'
import type { RunAbandonmentInspectionAnswer, RunAbandonmentInspectionTarget } from '../runAbandonmentInspection'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'

/** The one recorded read is aborted after this long: a hung request can never keep a stale answer or a pending state current. */
export const RUN_ABANDONMENT_INSPECTION_TIMEOUT_MS = 10_000

/** `read`: the request failed. `not-found`: the host no longer knows this run. `timeout`: it did not answer in time. `invalid`: the answer is not coherent. */
export type RunAbandonmentInspectionFailure = 'read' | 'not-found' | 'timeout' | 'invalid'

export interface UseRunAbandonmentInspectionResult {
  /** Whether the region of this run visit is open. */
  open: boolean
  /** The lifetime of the current read visit (null while closed): the request, its pending state and its answer belong to it. */
  visitKey: string | null
  pending: boolean
  /** The validated answer of this visit's one read; never together with a failure. */
  answer: RunAbandonmentInspectionAnswer | null
  failure: RunAbandonmentInspectionFailure | null
  /** Opens the region and reads the recorded operation once; an open region is left as it is. */
  show: () => void
  /** Closes the opening that created this callback, discarding its answer and the request in flight. */
  close: () => void
  /** Reads the operation again as a new visit: the earlier visit, its answer and its request end before the new read starts. */
  reload: () => void
}

interface OpeningFrame {
  open: boolean
  serial: number
  visit: number
  /** The run the region was opened for; every answer of this opening is judged against it. */
  target: RunAbandonmentInspectionTarget | null
}

interface VisitFrame {
  pending: boolean
  answer: RunAbandonmentInspectionAnswer | null
  failure: RunAbandonmentInspectionFailure | null
}

const createClosed = (): OpeningFrame => ({ open: false, serial: 0, visit: 0, target: null })
const createPendingVisit = (): VisitFrame => ({ pending: true, answer: null, failure: null })

const isNotFound = (caught: unknown): boolean =>
  typeof caught === 'object' && caught !== null && (caught as { status?: unknown }).status === 404

/**
 * The explicit recorded inspection of one run's abandonment (ADR-0071). Lifetimes nest and each owns its callbacks and
 * results: the run visit of the surrounding surface (`ownerKey`, which names the project and run), each opening and each read visit (an
 * open or a reload). A replacement derives its first frame in the same render, so another run or project, closing, reopening, a reload,
 * a return to an earlier run (A-B-A) or unmounting can never show or settle anything of an earlier lifetime, and a retained callback of
 * an ended lifetime starts and clears nothing.
 *
 * Nothing is read until the owner opens or reloads the region. Nothing is polled, retried automatically, prefetched, cached across
 * lifetimes, refreshed by live events, logged, stored or exported, and nothing here sends a command, reads the live abandonment status, blockers
 * or eligibility, or touches any mutation hook. The one GET carries a real AbortSignal: ending its visit or its ten-second bound
 * aborts the request itself, not only its continuation.
 */
export function useRunAbandonmentInspection(
  ownerKey: string | null,
  target: RunAbandonmentInspectionTarget | null,
): UseRunAbandonmentInspectionResult {
  const anchorOwner = useOwnedLifetime(target === null ? null : ownerKey)
  const [opening, commitOpening] = useOwnedState(anchorOwner, createClosed)
  const isOpen = anchorOwner.key !== null && opening.open && opening.target !== null
  const visitKey = isOpen ? JSON.stringify([anchorOwner.key, opening.serial, opening.visit]) : null
  const visitOwner = useOwnedLifetime(visitKey)
  const [frame, commitFrame] = useOwnedState(visitOwner, createPendingVisit)

  const latest = useRef({ target })
  useLayoutEffect(() => {
    latest.current = { target }
  })

  const show = useCallback(() => {
    const frozen = latest.current.target
    if (!anchorOwner.isActive() || anchorOwner.key === null || frozen === null) {
      return
    }

    commitOpening((previous) => (previous.open ? previous : { open: true, serial: previous.serial + 1, visit: 1, target: frozen }))
  }, [anchorOwner, commitOpening])

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

  const frozenTarget = isOpen ? opening.target : null
  useEffect(() => {
    if (visitOwner.key === null || frozenTarget === null) {
      return undefined
    }

    const isCurrent = visitOwner.begin('operation')
    const controller = new AbortController()
    let finished = false
    let timer: number | undefined

    const settle = (settled: VisitFrame) => {
      if (finished) {
        return
      }

      finished = true
      window.clearTimeout(timer)
      if (isCurrent()) {
        commitFrame(() => settled)
      }
    }

    timer = window.setTimeout(() => {
      settle({ pending: false, answer: null, failure: 'timeout' })
      controller.abort()
    }, RUN_ABANDONMENT_INSPECTION_TIMEOUT_MS)

    let request: Promise<unknown>
    try {
      request = runAbandonmentInspectionClient(controller.signal).readAbandonment(frozenTarget.runId)
    } catch (caught) {
      request = Promise.reject(caught)
    }

    request.then(
      (raw) => {
        const accepted = toRunAbandonmentInspection(raw, frozenTarget)
        settle(
          accepted === null
            ? { pending: false, answer: null, failure: 'invalid' }
            : { pending: false, answer: accepted, failure: null },
        )
      },
      (caught: unknown) => settle({ pending: false, answer: null, failure: isNotFound(caught) ? 'not-found' : 'read' }),
    )

    return () => {
      finished = true
      window.clearTimeout(timer)
      controller.abort()
    }
  }, [visitOwner, frozenTarget, commitFrame])

  return {
    open: isOpen,
    visitKey,
    pending: isOpen && frame.pending,
    answer: isOpen ? frame.answer : null,
    failure: isOpen ? frame.failure : null,
    show,
    close,
    reload,
  }
}
