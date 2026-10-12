import { useCallback, useLayoutEffect, useRef, useState } from 'react'
import { gitHubStoredAuthenticationClient } from '../../../api/clients'
import { toGitHubStoredAuthentication } from '../gitHubStoredAuthentication'
import type { GitHubStoredAuthentication } from '../gitHubStoredAuthentication'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'
import type { OwnedLifetime } from './useOwnedLifetime'

export type GitHubStoredAuthenticationPhase =
  | { kind: 'idle' }
  | { kind: 'pending' }
  /** `read`: the request failed. `invalid`: the host answered, but not with a coherent fixed-context status. */
  | { kind: 'failed'; reason: 'read' | 'invalid' }
  | { kind: 'result'; answer: GitHubStoredAuthentication }

export interface UseGitHubStoredAuthenticationResult {
  open: boolean
  phase: GitHubStoredAuthenticationPhase
  /** Opens the region without reading anything. */
  show: () => void
  /** Closes the opening that created this callback, discarding its answer and any read still in flight. */
  close: () => void
  /** The one explicit read (check or recheck). A no-op while collapsed, while one is in flight, or from an ended opening. */
  check: () => void
}

interface OpenFrame {
  open: boolean
  /** Counts openings, so every opening is its own lifetime even after returning to the same state. */
  serial: number
}

const IDLE: GitHubStoredAuthenticationPhase = { kind: 'idle' }
const PENDING: GitHubStoredAuthenticationPhase = { kind: 'pending' }

const createIdle = (): GitHubStoredAuthenticationPhase => IDLE

/**
 * The explicit, read-only observation of the default stored GitHub CLI authentication (ADR-0038). Two owned lifetimes: the
 * mounted component (its end, including the session ending and the unmount, ends everything) and each opening (closing and
 * reopening is a new lifetime that starts unchecked). Nothing reads except `check()`: not mount, opening, a project change,
 * a reconnect, a timer or a background refresh. A completion that belongs to an ended opening, or that was superseded by a
 * newer check of the same opening, is ignored (the host request itself still happened), a second activation while one read
 * is in flight is ignored, and a callback retained from an ended opening starts and closes nothing of its successor. While a
 * check is pending or after it failed no earlier answer is shown, and an answer is accepted only whole for the fixed
 * host and context.
 */
export function useGitHubStoredAuthentication(): UseGitHubStoredAuthenticationResult {
  const [frame, setFrame] = useState<OpenFrame>({ open: false, serial: 0 })
  const owner = useOwnedLifetime(frame.open ? `opening-${frame.serial}` : null)
  const [phase, commit] = useOwnedState(owner, createIdle)
  const inFlight = useRef<{ owner: OwnedLifetime; isCurrent: () => boolean } | null>(null)

  // The committed frame, so a callback created by an earlier render reads what a later one committed.
  const committed = useRef(frame)
  useLayoutEffect(() => {
    committed.current = frame
  })

  const show = useCallback(() => {
    setFrame((previous) => (previous.open ? previous : { open: true, serial: previous.serial + 1 }))
  }, [])

  const serial = frame.serial
  const close = useCallback(() => {
    setFrame((previous) => (previous.open && previous.serial === serial ? { open: false, serial: previous.serial } : previous))
  }, [serial])

  const check = useCallback(() => {
    if (!owner.isActive() || owner.key === null) {
      return
    }

    if (inFlight.current?.owner === owner && inFlight.current.isCurrent()) {
      return
    }

    const isCurrent = owner.begin('check')
    inFlight.current = { owner, isCurrent }
    const settle = (next: GitHubStoredAuthenticationPhase) => {
      if (isCurrent()) {
        inFlight.current = null
        commit(() => next)
      }
    }

    commit(() => PENDING)
    gitHubStoredAuthenticationClient()
      .getGitHubStoredAuthentication()
      .then(
        (response) => {
          const answer = toGitHubStoredAuthentication(response)
          settle(answer === null ? { kind: 'failed', reason: 'invalid' } : { kind: 'result', answer })
        },
        () => settle({ kind: 'failed', reason: 'read' }),
      )
  }, [owner, commit])

  return { open: frame.open, phase: frame.open ? phase : IDLE, show, close, check }
}
