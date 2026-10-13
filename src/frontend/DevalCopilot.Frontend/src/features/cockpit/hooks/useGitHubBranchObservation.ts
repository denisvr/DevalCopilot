import { useCallback, useRef } from 'react'
import { gitHubBranchObservationClient } from '../../../api/clients'
import { branchObservationScopeKey, toBranchObservation } from '../gitHubBranchObservation'
import type { BranchObservationReading, BranchObservationScope } from '../gitHubBranchObservation'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'
import type { OwnedLifetime } from './useOwnedLifetime'

export type GitHubBranchObservationPhase =
  | { kind: 'idle' }
  | { kind: 'pending' }
  /** `read`: the request failed. `invalid`: the host answered, but not with a coherent answer for exactly this source and target. */
  | { kind: 'failed'; reason: 'read' | 'invalid' }
  | { kind: 'result'; reading: BranchObservationReading }

export interface UseGitHubBranchObservationResult {
  /** Whether the region is expanded; false (collapsed) for every newly committed scope. */
  open: boolean
  phase: GitHubBranchObservationPhase
  /** Opens the region without reading anything. */
  show: () => void
  /** Closes the opening that created this callback, discarding its answer and any read still in flight. */
  close: () => void
  /** The one explicit read (check or recheck). A no-op while collapsed, while one is in flight, after a close or from an ended opening. */
  check: () => void
}

// What the committed scope owns: whether its region is expanded, and how many times it was opened, so each opening is a lifetime.
interface OpenFrame {
  open: boolean
  serial: number
}

const IDLE: GitHubBranchObservationPhase = { kind: 'idle' }
const PENDING: GitHubBranchObservationPhase = { kind: 'pending' }

const createOpenFrame = (): OpenFrame => ({ open: false, serial: 0 })
const createIdle = (): GitHubBranchObservationPhase => IDLE

/**
 * The explicit, read-only observation of the repository identity and the exact recorded branch tip of one recorded ADR-0040 target (ADR-0042). Two
 * owned lifetimes: the committed scope (the exact run, operation, commit, branch and target; collapsed by default; another scope, a
 * return to an earlier one or the unmount ends it) and each opening of that scope (closing and reopening is a new lifetime that starts
 * unchecked). Nothing reads except `check()`: not mount, opening, a project, run or target change, a reconnect, a timer or a
 * background refresh, and nothing is retried.
 *
 * A completion that belongs to an ended opening, or that was superseded, is ignored (the host request itself still happened). A second
 * activation while one read is in flight, two activations in the same batch, and any activation after `close` in the same batch send
 * nothing, and a callback retained from an ended opening starts and closes nothing of its successor. While a check is pending or after
 * it failed no earlier answer is shown, and an answer is accepted only whole and only for exactly this source and target.
 */
export function useGitHubBranchObservation(scope: BranchObservationScope | null): UseGitHubBranchObservationResult {
  const scopeKey = scope === null ? null : branchObservationScopeKey(scope)
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
    const settle = (next: GitHubBranchObservationPhase) => {
      if (isCurrent()) {
        inFlight.current = null
        commit(() => next)
      }
    }

    commit(() => PENDING)
    gitHubBranchObservationClient()
      .getGitHubBranchObservation(scope.source.runId)
      .then(
        (response) => {
          const reading = toBranchObservation(response, scope)
          settle(reading === null ? { kind: 'failed', reason: 'invalid' } : { kind: 'result', reading })
        },
        () => settle({ kind: 'failed', reason: 'read' }),
      )
  }, [scope, owner, commit])

  return { open, phase: open ? phase : IDLE, show, close, check }
}
