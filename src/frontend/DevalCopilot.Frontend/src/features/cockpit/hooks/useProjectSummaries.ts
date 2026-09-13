import { useCallback, useEffect, useState } from 'react'
import type { ProjectRunSummaryResponse } from '../../../api/clients'
import { projectsClient } from '../../../api/clients'

interface UseProjectSummariesResult {
  projects: ProjectRunSummaryResponse[]
  loading: boolean
  error: string | null
  refresh: () => void
}

/**
 * @param ready Set to false to skip fetching entirely — e.g. while the launch session is
 * still resolving. Requests must never fire before a session exists to authenticate them.
 */
export function useProjectSummaries(ready: boolean = true): UseProjectSummariesResult {
  const [projects, setProjects] = useState<ProjectRunSummaryResponse[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [refreshToken, setRefreshToken] = useState(0)

  const refresh = useCallback(() => setRefreshToken((token) => token + 1), [])

  useEffect(() => {
    if (!ready) {
      return
    }

    let cancelled = false
    setLoading(true)

    projectsClient()
      .getProjectRunSummaries()
      .then((result) => {
        if (!cancelled) {
          setProjects(result)
          setError(null)
        }
      })
      .catch((caught: unknown) => {
        if (!cancelled) {
          setError(caught instanceof Error ? caught.message : 'Failed to load projects.')
        }
      })
      .finally(() => {
        if (!cancelled) {
          setLoading(false)
        }
      })

    return () => {
      cancelled = true
    }
  }, [ready, refreshToken])

  return { projects, loading, error, refresh }
}
