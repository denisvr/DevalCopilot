import { useCallback, useEffect, useLayoutEffect, useRef } from 'react'
import { collaborationReplyClient } from '../../../api/clients'
import { toCollaborationReply } from '../collaborationReply'
import type { CollaborationReply, CollaborationReplySource } from '../collaborationReply'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'
import type { OwnedLifetime } from './useOwnedLifetime'

/** `read`: the request failed. `invalid`: the host answered, but not with a coherent reply for exactly this source. */
export type CollaborationReplyFailure = 'read' | 'invalid'

export interface UseCollaborationReplyResult {
  /** The one source whose recorded reply is inspected, or null when no inspector is open. */
  source: CollaborationReplySource | null
  pending: boolean
  /** The validated relationship; present only for the current source and never together with a failure. */
  reply: CollaborationReply | null
  failure: CollaborationReplyFailure | null
  /** Opens the inspector for exactly this observed source and reads it once; another source replaces the one inspected. */
  inspect: (source: CollaborationReplySource) => void
  /** Closes the inspector and discards its answer, failure and any read still in flight. */
  close: () => void
  /** Reads the same source again after a failed read; nothing else ever repeats a read. */
  retry: () => void
}

// What the history open lifetime owns: the one inspected source and how many times one was opened, so each opening is a lifetime.
interface SelectionFrame {
  source: CollaborationReplySource | null
  serial: number
}

// What one inspector lifetime owns: its single read in flight, the validated answer or a failure.
interface ReplyFrame {
  pending: boolean
  reply: CollaborationReply | null
  failure: CollaborationReplyFailure | null
}

const createSelectionFrame = (): SelectionFrame => ({ source: null, serial: 0 })

const createReplyFrame = (): ReplyFrame => ({ pending: true, reply: null, failure: null })

/**
 * The explicit, read-only inspection of one recorded message's reply parent. Two lifetimes own it: the committed run's open history
 * lifetime (`historyLifetime`, null while collapsed or without a run; another run, closing, reopening, reloading or unmounting ends
 * it and the inspector with it) and each inspector opening (a different source, closing and reopening the same one are new
 * lifetimes, so a read for A can never settle B or a later A). A replacement derives its own first frame in the same render, an
 * older or ended read can neither overwrite nor settle a newer one and the callbacks of an ended lifetime start nothing. The answer
 * is accepted only whole and only when coherent for exactly the observed source; otherwise it is an explicit failure and `retry`
 * reads the same source again. Nothing is read on render for a row, followed recursively, polled, retried automatically or cached
 * across lifetimes.
 */
export function useCollaborationReply(historyLifetime: string | null): UseCollaborationReplyResult {
  const historyOwner = useOwnedLifetime(historyLifetime)
  const [selection, commitSelection] = useOwnedState(historyOwner, createSelectionFrame)
  const source = historyLifetime === null ? null : selection.source
  const owner = useOwnedLifetime(source === null ? null : JSON.stringify([historyLifetime, source.messageId, selection.serial]))
  const [frame, commit] = useOwnedState(owner, createReplyFrame)

  // The frame this render committed and the one read in flight, both keyed by their lifetime, so a callback retained from an earlier
  // lifetime can never act on a later one and two calls in the same tick send one request.
  const displayed = useRef<{ owner: OwnedLifetime; frame: ReplyFrame } | null>(null)
  const inFlight = useRef<{ owner: OwnedLifetime; isCurrent: () => boolean } | null>(null)

  useLayoutEffect(() => {
    displayed.current = { owner, frame }
  })

  const inspect = useCallback(
    (next: CollaborationReplySource) => {
      if (!historyOwner.isActive() || historyOwner.key === null) {
        return
      }

      commitSelection((previous) =>
        previous.source?.messageId === next.messageId ? previous : { source: next, serial: previous.serial + 1 })
    },
    [historyOwner, commitSelection],
  )

  // Bound to the inspector lifetime and selection that created it: once that inspector ended, or a replacement was queued in the same
  // batch (a newer serial), the callback closes nothing, so a retained close can never end its successor.
  const serial = selection.serial
  const close = useCallback(() => {
    if (!owner.isActive() || owner.key === null) {
      return
    }

    commitSelection((previous) => (previous.serial === serial && previous.source !== null ? { ...previous, source: null } : previous))
  }, [owner, serial, commitSelection])

  const read = useCallback(
    (target: CollaborationReplySource) => {
      const isCurrent = owner.begin('reply')
      inFlight.current = { owner, isCurrent }
      const settle = (update: (previous: ReplyFrame) => ReplyFrame) => {
        if (isCurrent()) {
          inFlight.current = null
          commit(update)
        }
      }

      commit(() => ({ pending: true, reply: null, failure: null }))
      collaborationReplyClient()
        .getCollaborationHistoryReply(target.runId, target.messageId)
        .then(
          (response) => {
            const accepted = toCollaborationReply(response, target)
            settle(() => accepted === null
              ? { pending: false, reply: null, failure: 'invalid' }
              : { pending: false, reply: accepted, failure: null })
          },
          () => settle(() => ({ pending: false, reply: null, failure: 'read' })),
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
    reply: source !== null ? frame.reply : null,
    failure: source !== null ? frame.failure : null,
    inspect,
    close,
    retry,
  }
}
