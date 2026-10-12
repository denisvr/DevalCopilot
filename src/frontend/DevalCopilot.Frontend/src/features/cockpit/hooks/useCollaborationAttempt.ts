import { useCallback, useEffect, useLayoutEffect, useRef } from 'react'
import { collaborationAttemptClient } from '../../../api/clients'
import { toCollaborationAttempt } from '../collaborationAttempt'
import type { CollaborationAttempt, CollaborationAttemptSource } from '../collaborationAttempt'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'
import type { OwnedLifetime } from './useOwnedLifetime'

/** `read`: the request failed. `invalid`: the host answered, but not with a coherent attribution for exactly this source. */
export type CollaborationAttemptFailure = 'read' | 'invalid'

export interface UseCollaborationAttemptResult {
  /** The one source whose recorded attempt attribution is inspected, or null when no inspector is open. */
  source: CollaborationAttemptSource | null
  pending: boolean
  /** The validated attribution; present only for the current source and never together with a failure. */
  attempt: CollaborationAttempt | null
  failure: CollaborationAttemptFailure | null
  /** Opens the inspector for exactly this observed source and reads it once; another source replaces the one inspected. */
  inspect: (source: CollaborationAttemptSource) => void
  /** Closes the inspector that created this callback, discarding its answer, failure and any read still in flight. */
  close: () => void
  /** Reads the same source again after a failed read; nothing else ever repeats a read. */
  retry: () => void
}

// What the history open lifetime owns: the one inspected source and how many times one was opened, so each opening is a lifetime.
interface SelectionFrame {
  source: CollaborationAttemptSource | null
  serial: number
}

// What one inspector lifetime owns: its single read in flight, the validated answer or a failure.
interface AttemptFrame {
  pending: boolean
  attempt: CollaborationAttempt | null
  failure: CollaborationAttemptFailure | null
}

const createSelectionFrame = (): SelectionFrame => ({ source: null, serial: 0 })

const createAttemptFrame = (): AttemptFrame => ({ pending: true, attempt: null, failure: null })

/**
 * The explicit, read-only inspection of one recorded message's referenced Agent attempt (ADR-0037). Two lifetimes own it: the committed
 * run's open history lifetime (`historyLifetime`, null while collapsed or without a run; another run, closing, reopening, reloading or
 * unmounting ends it and the inspector with it) and each inspector opening (a different source, and closing then inspecting the same
 * one, are new lifetimes, so a read for A can never settle B or a later A). A replacement derives its own first frame in the same
 * render, an older or ended read can neither overwrite nor settle a newer one, and the callbacks of an ended lifetime start nothing:
 * `close` is bound to the inspector that created it and its update is guarded by the selection serial, so even a replacement queued in
 * the same batch cannot be closed by a retained callback. The answer is accepted only whole and only when coherent for exactly the
 * observed source; otherwise it is an explicit failure and `retry` reads the same source again. Nothing is read on render for a row,
 * polled, retried automatically or cached across lifetimes, and it is independent of the reply inspector.
 */
export function useCollaborationAttempt(historyLifetime: string | null): UseCollaborationAttemptResult {
  const historyOwner = useOwnedLifetime(historyLifetime)
  const [selection, commitSelection] = useOwnedState(historyOwner, createSelectionFrame)
  const source = historyLifetime === null ? null : selection.source
  const owner = useOwnedLifetime(source === null ? null : JSON.stringify([historyLifetime, source.messageId, selection.serial]))
  const [frame, commit] = useOwnedState(owner, createAttemptFrame)

  // The frame this render committed and the one read in flight, both keyed by their lifetime, so a callback retained from an earlier
  // lifetime can never act on a later one and two calls in the same tick send one request.
  const displayed = useRef<{ owner: OwnedLifetime; frame: AttemptFrame } | null>(null)
  const inFlight = useRef<{ owner: OwnedLifetime; isCurrent: () => boolean } | null>(null)

  useLayoutEffect(() => {
    displayed.current = { owner, frame }
  })

  const inspect = useCallback(
    (next: CollaborationAttemptSource) => {
      if (!historyOwner.isActive() || historyOwner.key === null) {
        return
      }

      commitSelection((previous) =>
        previous.source?.messageId === next.messageId ? previous : { source: next, serial: previous.serial + 1 })
    },
    [historyOwner, commitSelection],
  )

  const serial = selection.serial
  const close = useCallback(() => {
    if (!owner.isActive() || owner.key === null) {
      return
    }

    commitSelection((previous) => (previous.serial === serial && previous.source !== null ? { ...previous, source: null } : previous))
  }, [owner, serial, commitSelection])

  const read = useCallback(
    (target: CollaborationAttemptSource) => {
      const isCurrent = owner.begin('attempt')
      inFlight.current = { owner, isCurrent }
      const settle = (update: (previous: AttemptFrame) => AttemptFrame) => {
        if (isCurrent()) {
          inFlight.current = null
          commit(update)
        }
      }

      commit(() => ({ pending: true, attempt: null, failure: null }))
      collaborationAttemptClient()
        .getCollaborationHistoryAttempt(target.runId, target.messageId)
        .then(
          (response) => {
            const accepted = toCollaborationAttempt(response, target)
            settle(() => accepted === null
              ? { pending: false, attempt: null, failure: 'invalid' }
              : { pending: false, attempt: accepted, failure: null })
          },
          () => settle(() => ({ pending: false, attempt: null, failure: 'read' })),
        )
    },
    [owner, commit],
  )

  useEffect(() => {
    if (owner.key !== null && source !== null) {
      read(source)
    }
  }, [owner, source, read])

  const retry = useCallback(() => {
    const current = displayed.current
    if (!owner.isActive() || owner.key === null || current === null || current.owner !== owner || source === null) {
      return
    }

    if (current.frame.failure === null || current.frame.pending || (inFlight.current?.owner === owner && inFlight.current.isCurrent())) {
      return
    }

    read(source)
  }, [owner, read, source])

  return {
    source,
    pending: source !== null && frame.pending,
    attempt: source !== null ? frame.attempt : null,
    failure: source !== null ? frame.failure : null,
    inspect,
    close,
    retry,
  }
}
