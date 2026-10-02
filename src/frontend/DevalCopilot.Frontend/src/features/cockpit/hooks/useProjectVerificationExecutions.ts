import { useCallback, useEffect, useRef } from 'react'
import { projectVerificationExecutionsClient } from '../../../api/clients'
import type { VerificationExecutionResponse } from '../../../api/clients'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'

const POLL_INTERVAL_MS = 1000

interface ExecutionsFrame {
  executions: VerificationExecutionResponse[]
  error: string | null
}

function createFrame(): ExecutionsFrame {
  return { executions: [], error: null }
}

/** Polls bounded execution metadata only while a claimed verification is pending or running.
 *
 * The list and error belong to the current project's lifetime. Polling is governed only by the newest
 * accepted read of that lifetime: a Running answer starts (or restarts) the single one-second chain, a
 * terminal, empty or failed answer ends it, and a superseded answer, an ended lifetime or an unmount
 * can neither schedule nor keep it alive. A `refresh` retained from a replaced project starts no request. */
export function useProjectVerificationExecutions(projectId: string | null) {
  const owner = useOwnedLifetime(projectId)
  const [frame, commit] = useOwnedState(owner, createFrame)
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined)

  const refresh = useCallback(
    async function read(): Promise<VerificationExecutionResponse[]> {
      if (!projectId || !owner.isActive()) {
        return []
      }

      const isCurrent = owner.begin('read')
      const schedule = (running: boolean) => {
        clearTimeout(timer.current)
        timer.current = running ? setTimeout(() => void read(), POLL_INTERVAL_MS) : undefined
      }
      try {
        const next = await projectVerificationExecutionsClient().getProjectVerificationExecutions(projectId)
        if (isCurrent()) {
          commit(() => ({ executions: next, error: null }))
          schedule(next.some(execution => execution.status === 'Running'))
        }
        return next
      } catch {
        if (isCurrent()) {
          commit((previous) => ({ ...previous, error: 'Verification status could not be loaded.' }))
          schedule(false)
        }
        return []
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
  }, [refresh])

  return { ...frame, refresh }
}
