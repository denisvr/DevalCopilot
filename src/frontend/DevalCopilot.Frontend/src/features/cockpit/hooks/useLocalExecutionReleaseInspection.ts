import { useCallback, useEffect, useLayoutEffect, useRef } from 'react'
import { localExecutionReleaseInspectionClient } from '../../../api/clients'
import { toLocalExecutionReleaseInspection } from '../localExecutionReleaseInspection'
import type { LocalExecutionReleaseInspectionAnswer, LocalExecutionReleaseInspectionTarget } from '../localExecutionReleaseInspection'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'

/** The one receipt request is aborted after this long: a hung request can never keep a stale answer or a pending state current. */
export const LOCAL_EXECUTION_RELEASE_INSPECTION_TIMEOUT_MS = 10_000

/** `read`: the request failed. `not-found`: the host no longer knows this run or step. `timeout`: it did not answer in time. `invalid`: the answer is not coherent. */
export type LocalExecutionReleaseInspectionFailure = 'read' | 'not-found' | 'timeout' | 'invalid'

export interface UseLocalExecutionReleaseInspectionResult {
  /** Whether the region of this step visit is open. */
  open: boolean
  /** The lifetime of the current read visit (null while closed): the request, its pending state and its answer belong to it. */
  visitKey: string | null
  pending: boolean
  /** The validated answer of this visit's one read; never together with a failure. */
  answer: LocalExecutionReleaseInspectionAnswer | null
  failure: LocalExecutionReleaseInspectionFailure | null
  /** Opens the region and reads the receipt once; an open region is left as it is. */
  show: () => void
  /** Closes the opening that created this callback, discarding its answer and the request in flight. */
  close: () => void
  /** Reads the receipt again as a new visit: the earlier visit, its answer and its request end before the new read starts. */
  reload: () => void
}

interface OpeningFrame {
  open: boolean
  serial: number
  visit: number
  /** What the accepted index said about the step when the region was opened; every answer of this opening is judged against it. */
  target: LocalExecutionReleaseInspectionTarget | null
}

interface VisitFrame {
  pending: boolean
  answer: LocalExecutionReleaseInspectionAnswer | null
  failure: LocalExecutionReleaseInspectionFailure | null
}

const createClosed = (): OpeningFrame => ({ open: false, serial: 0, visit: 0, target: null })
const createPendingVisit = (): VisitFrame => ({ pending: true, answer: null, failure: null })

const isNotFound = (caught: unknown): boolean =>
  typeof caught === 'object' && caught !== null && (caught as { status?: unknown }).status === 404

/**
 * The explicit recorded inspection of one proved step's Human quarantine-release receipt (ADR-0067). Lifetimes nest and each owns its
 * callbacks and results: the step visit of the surrounding inspector (`ownerKey`, which already names the session or run, the parent
 * opening and index visit, the step and its operation visit), each opening and each read visit (an open or a reload). A replacement
 * derives its first frame in the same render, so another step visit, closing, reopening, a reload, a return to an earlier one (A-B-A) or
 * unmounting can never show or settle anything of an earlier lifetime, and a retained callback of an ended lifetime starts and clears
 * nothing.
 *
 * Nothing is read until the owner opens or reloads the region: choosing the step reads no receipt. Nothing is polled, retried
 * automatically, prefetched, cached across lifetimes, logged, stored or exported, and nothing here sends a command, reads a release
 * candidate or touches the live control readers. The one GET carries a real AbortSignal: ending its visit or its ten-second bound aborts
 * the request itself, not only its continuation, and one request is in flight at a time: a request releases only its own guard.
 */
export function useLocalExecutionReleaseInspection(
  ownerKey: string | null,
  target: LocalExecutionReleaseInspectionTarget | null,
): UseLocalExecutionReleaseInspectionResult {
  const anchorOwner = useOwnedLifetime(target === null ? null : ownerKey)
  const [opening, commitOpening] = useOwnedState(anchorOwner, createClosed)
  const isOpen = anchorOwner.key !== null && opening.open && opening.target !== null
  const visitKey = isOpen ? JSON.stringify([anchorOwner.key, opening.serial, opening.visit]) : null
  const visitOwner = useOwnedLifetime(visitKey)
  const [frame, commitFrame] = useOwnedState(visitOwner, createPendingVisit)

  const latest = useRef({ target })
  const inFlight = useRef<object | null>(null)
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

    const isCurrent = visitOwner.begin('receipt')
    const controller = new AbortController()
    const flight = {}
    inFlight.current = flight
    let finished = false
    let timer: number | undefined
    const release = () => {
      if (inFlight.current === flight) {
        inFlight.current = null
      }
    }

    const settle = (settled: VisitFrame) => {
      if (finished) {
        return
      }

      finished = true
      window.clearTimeout(timer)
      release()
      if (isCurrent()) {
        commitFrame(() => settled)
      }
    }

    timer = window.setTimeout(() => {
      settle({ pending: false, answer: null, failure: 'timeout' })
      controller.abort()
    }, LOCAL_EXECUTION_RELEASE_INSPECTION_TIMEOUT_MS)

    let request: Promise<unknown>
    try {
      request = localExecutionReleaseInspectionClient(controller.signal).readReceipt(frozenTarget.runId, frozenTarget.stepId)
    } catch (caught) {
      request = Promise.reject(caught)
    }

    request.then(
      (raw) => {
        const accepted = toLocalExecutionReleaseInspection(raw, frozenTarget)
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
      release()
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
