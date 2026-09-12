import { useCallback, useEffect, useState } from 'react'
import type { ProjectRunSummaryResponse } from '../../../api/clients'
import { projectsClient } from '../../../api/clients'

interface UseProjectSummariesResult {
  projects: ProjectRunSummaryResponse[]
  loading: boolean
  error: string | null
  refresh: () => void
}

export function useProjectSummaries(): UseProjectSummariesResult {
  const [projects, setProjects] = useState<ProjectRunSummaryResponse[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [refreshToken, setRefreshToken] = useState(0)

  const refresh = useCallback(() => setRefreshToken((token) => token + 1), [])

  useEffect(() => {
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
  }, [refreshToken])

  return { projects, loading, error, refresh }
}
