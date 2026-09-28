import { useCallback, useEffect, useState } from 'react'
import type { CodexModelCatalogResponse } from '../../../api/clients'
import { codexModelCatalogClient } from '../../../api/clients'

interface UseCodexModelCatalogResult {
  catalog: CodexModelCatalogResponse | null
  loading: boolean
  error: string | null
  refresh: () => void
}

/**
 * Fetches the host-scoped Codex model and reasoning-effort catalog once (never on a recurring
 * interval — this is an explicit, on-demand read, not scheduled polling). `refresh` lets a caller
 * request a fresh catalog, e.g. from a manual "Refresh" control. This is catalog evidence only —
 * never selected or effective configuration, authentication readiness, or a guarantee that a
 * listed model remains available at dispatch.
 */
export function useCodexModelCatalog(ready: boolean = true): UseCodexModelCatalogResult {
  const [catalog, setCatalog] = useState<CodexModelCatalogResponse | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [refreshToken, setRefreshToken] = useState(0)

  const refresh = useCallback(() => {
    setCatalog(null)
    setError(null)
    setLoading(true)
    setRefreshToken((token) => token + 1)
  }, [])

  useEffect(() => {
    if (!ready) {
      return
    }

    let cancelled = false
    codexModelCatalogClient()
      .getCodexModelCatalog()
      .then((result) => {
        if (!cancelled) {
          setCatalog(result)
          setError(null)
        }
      })
      .catch(() => {
        if (!cancelled) {
          // A failed refresh must never leave a previously observed catalog and retrieval time
          // on screen looking current — that would misrepresent stale data as a fresh read.
          setCatalog(null)
          setError('Codex model catalog is unavailable.')
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

  return { catalog, loading, error, refresh }
}
