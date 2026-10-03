import { useCallback, useEffect, useLayoutEffect, useRef } from 'react'
import { projectVerificationExecutionsClient } from '../../../api/clients'
import type { VerificationExecutionResponse } from '../../../api/clients'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'

const POLL_INTERVAL_MS = 1000

interface ExecutionsFrame {
  executions: VerificationExecutionResponse[]
  error: string | null
  // The refresh generation the newest accepted successful read belongs to, whether the newest accepted read failed, and
  // whether a read is in flight.
  readGeneration: number | null
  readFailed: boolean
  loading: boolean
}

function createFrame(): ExecutionsFrame {
  return { executions: [], error: null, readGeneration: null, readFailed: false, loading: false }
}

/** Polls bounded execution metadata only while a claimed verification is pending or running.
 *
 * The list and error belong to the current project's lifetime. Polling is governed only by the newest
 * accepted read of that lifetime: a Running answer starts (or restarts) the single one-second chain, a
 * terminal, empty or failed answer ends it, and a superseded answer, an ended lifetime or an unmount
 * can neither schedule nor keep it alive. A `refresh` retained from a replaced project starts no request.
 *
 * The project's owner may ask for a fresh read by advancing `refreshGeneration`; that read stays inside the same single chain.
 * The list is `current` only while no read is in flight (a poll, a refresh and a retained refresh included) and the newest accepted
 * read, begun for the present generation, succeeded; the cached list stays visible as history, but a consumer never treats it as
 * authority while a read is pending or after it failed. */
export function useProjectVerificationExecutions(projectId: string | null, refreshGeneration = 0) {
  const owner = useOwnedLifetime(projectId)
  const [frame, commit] = useOwnedState(owner, createFrame)
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined)

  // A read belongs to the generation that is current when it begins, so a continuation retained from an earlier generation
  // that starts a read after the project asked again still satisfies the present one.
  const generation = useRef(refreshGeneration)
  useLayoutEffect(() => {
    generation.current = refreshGeneration
  }, [refreshGeneration])

  const refresh = useCallback(
    async function read(): Promise<VerificationExecutionResponse[]> {
      if (!projectId || !owner.isActive()) {
        return []
      }

      const isCurrent = owner.begin('read')
      const readGeneration = generation.current
      commit((previous) => ({ ...previous, loading: true }))
      const schedule = (running: boolean) => {
        clearTimeout(timer.current)
        timer.current = running ? setTimeout(() => void read(), POLL_INTERVAL_MS) : undefined
      }
      try {
        const next = await projectVerificationExecutionsClient().getProjectVerificationExecutions(projectId)
        if (isCurrent()) {
          commit((previous) => ({ ...previous, executions: next, error: null, readGeneration, readFailed: false }))
          schedule(next.some(execution => execution.status === 'Running'))
        }
        return next
      } catch {
        if (isCurrent()) {
          commit((previous) => ({ ...previous, error: 'Verification status could not be loaded.', readFailed: true }))
          schedule(false)
        }
        return []
      } finally {
        if (isCurrent()) {
          commit((previous) => ({ ...previous, loading: false }))
        }
      }
    },
    [owner, projectId, commit],
  )

  useEffect(() => {
    void refresh()
    return () => {
      clearTimeout(timer.current)
      timer.current = undefined
    }
  }, [refresh, refreshGeneration])

  return { ...frame, current: frame.readGeneration === refreshGeneration && !frame.readFailed && !frame.loading, refresh }
}
