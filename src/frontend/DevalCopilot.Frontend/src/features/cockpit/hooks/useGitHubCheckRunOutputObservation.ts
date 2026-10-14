import { useCallback, useRef } from 'react'
import { gitHubCheckRunOutputObservationClient } from '../../../api/clients'
import { checkRunOutputScopeKey, toCheckRunOutputObservation } from '../gitHubCheckRunOutputObservation'
import type { CheckRunOutputReading, CheckRunOutputScope } from '../gitHubCheckRunOutputObservation'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'
import type { OwnedLifetime } from './useOwnedLifetime'

export type GitHubCheckRunOutputObservationPhase =
  | { kind: 'idle' }
  | { kind: 'pending' }
  /** `read`: the request failed. `invalid`: the host answered, but not with a coherent answer for exactly this source, target and check run. */
  | { kind: 'failed'; reason: 'read' | 'invalid' }
  | { kind: 'result'; reading: CheckRunOutputReading }

export interface UseGitHubCheckRunOutputObservationResult {
  /** Whether the region is expanded; false (collapsed) for every newly committed scope and parent reading. */
  open: boolean
  phase: GitHubCheckRunOutputObservationPhase
  /** Opens the region without reading anything. */
  show: () => void
  /** Closes the opening that created this callback, discarding its answer and any read still in flight. */
  close: () => void
  /** The one explicit read (check or recheck). A no-op while collapsed, while one is in flight, after a close or from an ended opening. */
  check: () => void
}

// What the committed scope and parent own: whether the region is expanded, and how many times it was opened, so each opening is a lifetime.
interface OpenFrame {
  open: boolean
  serial: number
}

const IDLE: GitHubCheckRunOutputObservationPhase = { kind: 'idle' }
const PENDING: GitHubCheckRunOutputObservationPhase = { kind: 'pending' }

const createOpenFrame = (): OpenFrame => ({ open: false, serial: 0 })
const createIdle = (): GitHubCheckRunOutputObservationPhase => IDLE

// Each parent reading object is one parent lifetime: a new reading (a recheck, a close or a scope change of the parent) is a new identity.
const parentIdentities = new WeakMap<object, number>()
let nextParentIdentity = 0
const parentIdentity = (parent: object): number => {
  let identity = parentIdentities.get(parent)
  if (identity === undefined) {
    identity = ++nextParentIdentity
    parentIdentities.set(parent, identity)
  }

  return identity
}

/**
 * The explicit, read-only inspection of the title, summary and text of one check run of the recorded commit of one recorded ADR-0040
 * target whose repository identity is bound (ADR-0047). Two owned lifetimes: the committed scope (the exact run, operation, commit,
 * branch, target and selected check run, together with the parent contexts reading it belongs to; collapsed by default; another scope or
 * check, a return to an earlier one, a new parent reading or the unmount ends it) and each opening of that scope (closing and reopening
 * is a new lifetime that starts unchecked). Nothing reads except `check()`: not mount, opening, a project, run, target, check or parent
 * change, a reconnect, a timer or a background refresh, and nothing is retried. The host reads the binding and re-proves the check's
 * association itself on every check; no sibling region's answer is consulted and the parent's answer is never authorization.
 *
 * A completion that belongs to an ended opening, or that was superseded, is ignored (the host request itself still happened). A second
 * activation while one read is in flight, two activations in the same batch, and any activation after `close` in the same batch send
 * nothing, and a callback retained from an ended opening starts and closes nothing of its successor. While a check is pending or after
 * it failed no earlier answer is shown, and an answer is accepted only whole and only for exactly this source, target and check run.
 */
export function useGitHubCheckRunOutputObservation(
  scope: CheckRunOutputScope | null,
  parent: object | null,
): UseGitHubCheckRunOutputObservationResult {
  const scopeKey = scope === null || parent === null ? null : JSON.stringify([checkRunOutputScopeKey(scope), parentIdentity(parent)])
  const scopeOwner = useOwnedLifetime(scopeKey)
  const [frame, commitFrame] = useOwnedState(scopeOwner, createOpenFrame)
  const open = scopeKey !== null && frame.open
  const serial = frame.serial
  const owner = useOwnedLifetime(open ? JSON.stringify([scopeKey, serial]) : null)
  const [phase, commit] = useOwnedState(owner, createIdle)

  // The one read in flight, keyed by its lifetime, so a callback retained from an earlier lifetime can never act on a later one and
  // two calls in the same tick send one request.
  const inFlight = useRef<{ owner: OwnedLifetime; isCurrent: () => boolean } | null>(null)
  // Openings closed by `close()` in this batch: the lifetime only ends when the closing commits, so a later call of the same batch must
  // still be refused.
  const closed = useRef(new WeakSet<OwnedLifetime>())

  const show = useCallback(() => {
    if (!scopeOwner.isActive() || scopeOwner.key === null) {
      return
    }

    commitFrame((previous) => (previous.open ? previous : { open: true, serial: previous.serial + 1 }))
  }, [scopeOwner, commitFrame])

  const close = useCallback(() => {
    if (owner.key !== null) {
      closed.current.add(owner)
    }

    commitFrame((previous) => (previous.open && previous.serial === serial ? { open: false, serial: previous.serial } : previous))
  }, [owner, commitFrame, serial])

  const check = useCallback(() => {
    if (scope === null || !owner.isActive() || owner.key === null || closed.current.has(owner) || inFlight.current?.owner === owner) {
      return
    }

    const isCurrent = owner.begin('check')
    inFlight.current = { owner, isCurrent }
    const settle = (next: GitHubCheckRunOutputObservationPhase) => {
      if (isCurrent()) {
        inFlight.current = null
        commit(() => next)
      }
    }

    commit(() => PENDING)
    gitHubCheckRunOutputObservationClient()
      .getGitHubCheckRunOutputObservation(scope.source.runId, scope.checkRunId)
      .then(
        (response) => {
          const reading = toCheckRunOutputObservation(response, scope)
          settle(reading === null ? { kind: 'failed', reason: 'invalid' } : { kind: 'result', reading })
        },
        () => settle({ kind: 'failed', reason: 'read' }),
      )
  }, [scope, owner, commit])

  return { open, phase: open ? phase : IDLE, show, close, check }
}
