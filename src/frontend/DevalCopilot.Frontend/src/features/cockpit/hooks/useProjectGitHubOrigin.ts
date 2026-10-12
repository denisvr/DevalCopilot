import { useCallback, useRef } from 'react'
import { projectGitHubOriginClient } from '../../../api/clients'
import { toProjectGitHubOrigin } from '../projectGitHubOrigin'
import type { ProjectGitHubOrigin } from '../projectGitHubOrigin'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'
import type { OwnedLifetime } from './useOwnedLifetime'

export type ProjectGitHubOriginPhase =
  | { kind: 'idle' }
  | { kind: 'pending' }
  /** `read`: the request failed. `invalid`: the host answered, but not with a coherent answer for exactly this project. */
  | { kind: 'failed'; reason: 'read' | 'invalid' }
  | { kind: 'result'; answer: ProjectGitHubOrigin }

export interface UseProjectGitHubOriginResult {
  /** Whether the region is expanded; false (collapsed) for every newly committed project. */
  open: boolean
  phase: ProjectGitHubOriginPhase
  /** Opens the region without reading anything. */
  show: () => void
  /** Closes the opening that created this callback, discarding its answer and any read still in flight. */
  close: () => void
  /** The one explicit read (inspect or recheck). A no-op while collapsed, while one is in flight, or from an ended lifetime. */
  check: () => void
}

// What the committed project owns: whether its region is expanded, and how many times it was opened, so each opening is a lifetime.
interface ProjectFrame {
  open: boolean
  serial: number
}

const IDLE: ProjectGitHubOriginPhase = { kind: 'idle' }
const PENDING: ProjectGitHubOriginPhase = { kind: 'pending' }

const createProjectFrame = (): ProjectFrame => ({ open: false, serial: 0 })
const createIdle = (): ProjectGitHubOriginPhase => IDLE

/**
 * The explicit, read-only observation of the selected project's repository-local GitHub origin (ADR-0039). Three owned lifetimes:
 * the mounted hook, the committed project (collapsed by default; another project, a return to an earlier one or the unmount ends
 * it) and each opening of that project (closing and reopening is a new lifetime that starts unchecked). Nothing reads except
 * `check()`: not mount, opening, a project change, a reconnect, a timer or a background refresh. A completion that belongs to an
 * ended lifetime, or that was superseded by a newer check of the same opening, is ignored (the host request itself still
 * happened), a second activation while one read is in flight is ignored, and a callback retained from an ended lifetime starts
 * and closes nothing of its successor. While a check is pending or after it failed no earlier answer is shown, and an answer is
 * accepted only whole and only for exactly the observed project.
 */
export function useProjectGitHubOrigin(projectId: string | null): UseProjectGitHubOriginResult {
  const projectOwner = useOwnedLifetime(projectId)
  const [project, commitProject] = useOwnedState(projectOwner, createProjectFrame)
  const open = projectId !== null && project.open
  const serial = project.serial
  const owner = useOwnedLifetime(open ? JSON.stringify([projectId, serial]) : null)
  const [phase, commit] = useOwnedState(owner, createIdle)

  // The one read in flight, keyed by its lifetime, so a callback retained from an earlier lifetime can never act on a later one
  // and two calls in the same tick send one request.
  const inFlight = useRef<{ owner: OwnedLifetime; isCurrent: () => boolean } | null>(null)

  const show = useCallback(() => {
    if (!projectOwner.isActive() || projectOwner.key === null) {
      return
    }

    commitProject((previous) => (previous.open ? previous : { open: true, serial: previous.serial + 1 }))
  }, [projectOwner, commitProject])

  const close = useCallback(() => {
    commitProject((previous) => (previous.open && previous.serial === serial ? { open: false, serial: previous.serial } : previous))
  }, [commitProject, serial])

  const check = useCallback(() => {
    if (projectId === null || !owner.isActive() || owner.key === null) {
      return
    }

    if (inFlight.current?.owner === owner && inFlight.current.isCurrent()) {
      return
    }

    const isCurrent = owner.begin('check')
    inFlight.current = { owner, isCurrent }
    const settle = (next: ProjectGitHubOriginPhase) => {
      if (isCurrent()) {
        inFlight.current = null
        commit(() => next)
      }
    }

    commit(() => PENDING)
    projectGitHubOriginClient()
      .getProjectGitHubOrigin(projectId)
      .then(
        (response) => {
          const answer = toProjectGitHubOrigin(response, projectId)
          settle(answer === null ? { kind: 'failed', reason: 'invalid' } : { kind: 'result', answer })
        },
        () => settle({ kind: 'failed', reason: 'read' }),
      )
  }, [projectId, owner, commit])

  return { open, phase: open ? phase : IDLE, show, close, check }
}
