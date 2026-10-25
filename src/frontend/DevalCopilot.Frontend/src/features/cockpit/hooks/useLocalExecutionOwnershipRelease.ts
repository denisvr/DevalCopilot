import { useCallback, useEffect, useLayoutEffect, useRef } from 'react'
import { localExecutionOwnershipReleaseCommandClient, localExecutionOwnershipReleaseReadClient } from '../../../api/clients'
import { toLocalExecutionOwnershipRelease } from '../localExecutionOwnershipRelease'
import type {
  LocalExecutionOwnershipReleaseAnswer,
  LocalExecutionReleaseReceipt,
  LocalExecutionReleaseTarget,
} from '../localExecutionOwnershipRelease'
import {
  LocalExecutionReleaseAnswerInvalid,
  RELEASE_BUSY,
  RELEASE_UNKNOWN_OUTCOME,
  classifyLocalExecutionReleaseFailure,
} from '../localExecutionOwnershipReleaseCopy'
import { BOUNDED_COMMAND_TIMEOUT_MS, startBoundedCommand } from './boundedCommand'
import type { BoundedCommand } from './boundedCommand'
import type { UseLocalExecutionSequenceResult } from './useLocalExecutionSequence'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'
import type { OwnedLifetime } from './useOwnedLifetime'

/** `read`: the request failed. `not-found`: the host no longer knows this run or step. `invalid`: the answer is not coherent for the request. */
export type LocalExecutionReleaseReadFailure = 'read' | 'not-found' | 'invalid'

/** A confirmation lives at most the host's two minutes; the host's own expiry is still the authority when the request arrives. */
export const RELEASE_CONFIRMATION_LIFETIME_MS = 120_000

export type LocalExecutionReleaseSubmission =
  | { phase: 'idle'; message: string | null }
  | { phase: 'submitting'; message: null }
  | { phase: 'released'; message: null; release: LocalExecutionReleaseReceipt }
  | { phase: 'refused'; message: string }
  | { phase: 'unknown'; message: string }

export interface UseLocalExecutionOwnershipReleaseResult {
  /** Whether the region of the current run is open. */
  open: boolean
  /** The lifetime of the chosen step's visit (null: none chosen): the read, the confirmation and the one submission belong to it. */
  visitKey: string | null
  selected: LocalExecutionReleaseTarget | null
  reading: boolean
  readFailure: LocalExecutionReleaseReadFailure | null
  /** The validated answer of this visit's one read; never together with a failure. */
  answer: LocalExecutionOwnershipReleaseAnswer | null
  /** Whether this visit's confirmation lifetime ended without a submission. */
  expired: boolean
  submission: LocalExecutionReleaseSubmission
  /** Opens the region; nothing is read until a step is chosen. An open region is left as it is. */
  show: () => void
  /** Closes the opening that created this callback, discarding its selection, read, confirmation and any request not yet sent. */
  close: () => void
  /** Chooses one step of the accepted status and reads only that step once; an unknown step is ignored. */
  select: (stepId: string) => void
  /** Clears the choice of the visit that created this callback; a later visit is never cleared by it. */
  clear: () => void
  /** Reads the same step again as a new visit: the old confirmation, outcome and in-flight read end before the read starts. */
  reload: () => void
  /** Submits this visit's one confirmation, once. A refusal, an unknown outcome or a success consumes it; only a new read renews it. */
  confirm: () => void
}

interface OpeningFrame {
  open: boolean
  serial: number
}

interface ChoiceFrame {
  chosen: { target: LocalExecutionReleaseTarget; visit: number } | null
  visits: number
}

interface VisitFrame {
  pending: boolean
  answer: LocalExecutionOwnershipReleaseAnswer | null
  failure: LocalExecutionReleaseReadFailure | null
  expired: boolean
  submission: LocalExecutionReleaseSubmission
}

const NOT_SUBMITTED: LocalExecutionReleaseSubmission = { phase: 'idle', message: null }

const createClosed = (): OpeningFrame => ({ open: false, serial: 0 })
const createNoChoice = (): ChoiceFrame => ({ chosen: null, visits: 0 })
const createPendingVisit = (): VisitFrame => ({ pending: true, answer: null, failure: null, expired: false, submission: NOT_SUBMITTED })

const NO_COMMANDS = { beginCommand: () => null }

const isNotFound = (caught: unknown): boolean =>
  typeof caught === 'object' && caught !== null && (caught as { status?: unknown }).status === 404

const sameOperation = (left: LocalExecutionReleaseTarget, right: LocalExecutionReleaseTarget): boolean =>
  left.stepId === right.stepId && left.sequenceId === right.sequenceId && left.operationId === right.operationId

/**
 * The explicit ownership release of one retained quarantine of the current run (ADR-0065). Lifetimes nest and each owns its callbacks
 * and results: the run, each opening, and each visit of one chosen step. A visit owns exactly one read, exactly one confirmation and at
 * most one submission, so choosing another step, reloading, closing, another run, a return to an earlier one (A-B-A) or unmounting can
 * never show or settle anything of an earlier lifetime, and a retained callback of an ended lifetime starts and clears nothing. Nothing
 * is read until the owner chooses a step, and nothing is polled, prefetched, cached across lifetimes or retried.
 *
 * Submitting consumes the confirmation: it is sent once with a real ten-second AbortController bound and never resent, and a refusal,
 * an unknown outcome or a success all leave the visit without a usable confirmation, so a late answer can never restore it. An unknown
 * outcome says so and never infers a rollback; only an explicit new read shows what was recorded. The submission is wrapped by the run's
 * status change barrier, so the one live status stays masked until a read that began after its transport settled has settled, and this
 * hook never marks the fence, the workspace or the capacity as released itself.
 *
 * `targets` is what the accepted status offers (null while the status is unknown, which keeps a chosen visit and its outcome alive).
 */
export function useLocalExecutionOwnershipRelease(
  runId: string | null,
  targets: readonly LocalExecutionReleaseTarget[] | null,
  reader: Pick<UseLocalExecutionSequenceResult, 'beginCommand'> | null,
): UseLocalExecutionOwnershipReleaseResult {
  const runOwner = useOwnedLifetime(runId)
  const [opening, commitOpening] = useOwnedState(runOwner, createClosed)
  const isOpen = runId !== null && opening.open
  const openKey = isOpen ? JSON.stringify([runId, opening.serial]) : null
  const openOwner = useOwnedLifetime(openKey)
  const [choice, commitChoice] = useOwnedState(openOwner, createNoChoice)

  const chosen = isOpen ? choice.chosen : null
  const selected = chosen?.target ?? null
  const visitNumber = chosen?.visit ?? null
  const visitKey = openKey === null || selected === null ? null : JSON.stringify([openKey, selected.stepId, visitNumber])
  const visitOwner = useOwnedLifetime(visitKey)
  const [visit, commitVisit] = useOwnedState(visitOwner, createPendingVisit)

  const latest = useRef({ targets, reader, selected, visit, visitOwner, visitNumber })
  const submitted = useRef<OwnedLifetime | null>(null)
  const command = useRef<{ owner: OwnedLifetime; sent: BoundedCommand } | null>(null)
  useLayoutEffect(() => {
    latest.current = { targets, reader, selected, visit, visitOwner, visitNumber }
  })

  const show = useCallback(() => {
    if (!runOwner.isActive() || runOwner.key === null) {
      return
    }

    commitOpening((previous) => (previous.open ? previous : { open: true, serial: previous.serial + 1 }))
  }, [runOwner, commitOpening])

  const { serial } = opening
  const close = useCallback(() => {
    if (!openOwner.isActive() || openOwner.key === null || latest.current.visit.submission.phase === 'submitting') {
      return
    }

    commitOpening((previous) => (previous.serial === serial && previous.open ? { ...previous, open: false } : previous))
  }, [openOwner, serial, commitOpening])

  const select = useCallback(
    (stepId: string) => {
      const known = latest.current.targets?.find((target) => target.stepId === stepId)
      if (!openOwner.isActive() || openOwner.key === null || known === undefined || latest.current.visit.submission.phase === 'submitting') {
        return
      }

      // Decided against the queued choice, so a Clear queued earlier in the same batch makes this a new visit.
      commitChoice((previous) =>
        previous.chosen?.target.stepId === stepId
          ? previous
          : { chosen: { target: known, visit: previous.visits + 1 }, visits: previous.visits + 1 },
      )
    },
    [openOwner, commitChoice],
  )

  const clear = useCallback(() => {
    if (visitNumber === null || !openOwner.isActive() || openOwner.key === null || latest.current.visit.submission.phase === 'submitting') {
      return
    }

    commitChoice((previous) => (previous.chosen?.visit === visitNumber ? { ...previous, chosen: null } : previous))
  }, [openOwner, visitNumber, commitChoice])

  const reload = useCallback(() => {
    if (visitNumber === null || !openOwner.isActive() || openOwner.key === null || latest.current.visit.submission.phase === 'submitting') {
      return
    }

    commitChoice((previous) => {
      if (previous.chosen?.visit !== visitNumber) {
        return previous
      }

      // The new visit is judged against what the status shows now for the same operation, never against an older reading of it.
      const fresh = latest.current.targets?.find((target) => sameOperation(target, previous.chosen!.target)) ?? previous.chosen.target
      return { chosen: { target: fresh, visit: previous.visits + 1 }, visits: previous.visits + 1 }
    })
  }, [openOwner, visitNumber, commitChoice])

  // A chosen operation that the accepted status no longer offers ends its visit instead of lingering.
  const chosenTarget = chosen?.target ?? null
  useEffect(() => {
    if (targets === null || chosenTarget === null) {
      return
    }

    if (!targets.some((target) => sameOperation(target, chosenTarget))) {
      clear()
    }
  }, [targets, chosenTarget, clear])

  useEffect(() => {
    if (visitOwner.key === null || runId === null || selected === null) {
      return
    }

    const isCurrent = visitOwner.begin('read')
    const controller = new AbortController()
    // The read has the same real ten-second bound as the command: at the bound the actual request is aborted and a fixed read failure is
    // settled, once. A transport that ignores the abort can still answer later, but never after the visit settled, so a late answer can
    // neither replace the failure nor restore a confirmation.
    let settled = false
    const settle = (frame: Pick<VisitFrame, 'pending' | 'answer' | 'failure'>) => {
      if (!settled && isCurrent()) {
        settled = true
        window.clearTimeout(bound)
        commitVisit((previous) => ({ ...previous, ...frame }))
      }
    }

    const bound = window.setTimeout(() => {
      settle({ pending: false, answer: null, failure: 'read' })
      controller.abort()
    }, BOUNDED_COMMAND_TIMEOUT_MS)

    commitVisit(createPendingVisit)
    localExecutionOwnershipReleaseReadClient(controller.signal)
      .readRelease(runId, selected.stepId)
      .then(
        (raw) => {
          const answer = toLocalExecutionOwnershipRelease(raw, selected)
          settle(answer === null ? { pending: false, answer: null, failure: 'invalid' } : { pending: false, answer, failure: null })
        },
        (caught: unknown) => settle({ pending: false, answer: null, failure: isNotFound(caught) ? 'not-found' : 'read' }),
      )

    return () => {
      settled = true
      window.clearTimeout(bound)
      controller.abort()
    }
  }, [visitOwner, runId, selected, commitVisit])

  const confirmable = visit.answer?.status === 'Available' && !visit.expired && visit.submission.phase === 'idle'
  useEffect(() => {
    if (!confirmable || visitOwner.key === null) {
      return
    }

    const isCurrent = visitOwner.begin('expiry')
    const timer = window.setTimeout(() => {
      if (isCurrent()) {
        commitVisit((previous) => ({ ...previous, expired: true }))
      }
    }, RELEASE_CONFIRMATION_LIFETIME_MS)
    return () => window.clearTimeout(timer)
  }, [confirmable, visitOwner, commitVisit])

  // The request in flight belongs to its visit: leaving the visit aborts the actual request and never resends it.
  useEffect(
    () => () => {
      if (command.current?.owner === visitOwner) {
        command.current.sent.cancel()
        command.current = null
      }
    },
    [visitOwner],
  )

  const confirm = useCallback(() => {
    const now = latest.current
    if (
      !visitOwner.isActive()
      || visitOwner.key === null
      || runId === null
      || now.visitOwner !== visitOwner
      || submitted.current === visitOwner
      || now.selected === null
      || now.visit.expired
      || now.visit.submission.phase !== 'idle'
      || now.visit.answer?.status !== 'Available'
    ) {
      return
    }

    const target = now.selected
    const candidate = now.visit.answer.candidate
    const ticket = (now.reader ?? NO_COMMANDS).beginCommand()
    if (ticket === null) {
      commitVisit((previous) => ({ ...previous, submission: { phase: 'idle', message: RELEASE_BUSY } }))
      return
    }

    submitted.current = visitOwner
    const isCurrent = visitOwner.begin('submit')
    const settle = (submission: LocalExecutionReleaseSubmission) => {
      if (isCurrent()) {
        commitVisit((previous) => ({ ...previous, submission }))
      }
    }

    commitVisit((previous) => ({ ...previous, submission: { phase: 'submitting', message: null } }))
    const sent = startBoundedCommand((signal) => localExecutionOwnershipReleaseCommandClient(signal).releaseRaw(runId, target.stepId, candidate))
    command.current = { owner: visitOwner, sent }
    sent.result
      .then(
        (raw) => {
          const answer = toLocalExecutionOwnershipRelease(raw, target)
          if (answer?.status !== 'Released') {
            throw new LocalExecutionReleaseAnswerInvalid()
          }

          settle({ phase: 'released', message: null, release: answer.release })
        },
      )
      .catch((caught: unknown) => {
        const failure = classifyLocalExecutionReleaseFailure(caught)
        settle(
          failure.kind === 'unknown'
            ? { phase: 'unknown', message: RELEASE_UNKNOWN_OUTCOME }
            : { phase: 'refused', message: failure.message },
        )
      })
      .finally(() => {
        if (command.current?.sent === sent) {
          command.current = null
        }

        ticket.settled()
      })
  }, [visitOwner, runId, commitVisit])

  const visible = selected !== null
  return {
    open: isOpen,
    visitKey,
    selected,
    reading: visible && visit.pending,
    readFailure: visible ? visit.failure : null,
    answer: visible ? visit.answer : null,
    expired: visible && visit.expired,
    submission: visible ? visit.submission : NOT_SUBMITTED,
    show,
    close,
    select,
    clear,
    reload,
    confirm,
  }
}
