import { useCallback, useEffect, useState } from 'react'
import { projectVerificationExecutionsClient } from '../../../api/clients'
import type { VerificationExecutionResponse } from '../../../api/clients'

const POLL_INTERVAL_MS = 1000

/** Polls bounded execution metadata only while a claimed verification is pending or running. */
export function useProjectVerificationExecutions(projectId: string | null) {
  const [executions, setExecutions] = useState<VerificationExecutionResponse[]>([])
  const [error, setError] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    if (!projectId) {
      setExecutions([])
      return []
    }

    try {
      const next = await projectVerificationExecutionsClient().getProjectVerificationExecutions(projectId)
      setExecutions(next)
      setError(null)
      return next
    } catch {
      setError('Verification status could not be loaded.')
      return []
    }
  }, [projectId])

  useEffect(() => {
    let cancelled = false
    let timer: ReturnType<typeof setTimeout> | undefined

    async function poll() {
      const next = await refresh()
      if (!cancelled && next.some(execution => execution.status === 'Running')) {
        timer = setTimeout(() => void poll(), POLL_INTERVAL_MS)
      }
    }

    void poll()
    return () => {
      cancelled = true
      if (timer) {
        clearTimeout(timer)
      }
    }
  }, [refresh])

  return { executions, error, refresh }
}
