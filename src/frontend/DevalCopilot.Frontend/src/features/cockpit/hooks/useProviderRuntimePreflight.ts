import { useCallback, useEffect, useState } from 'react'
import type { ProviderRuntimePreflightResponse } from '../../../api/clients'
import { providerRuntimePreflightClient } from '../../../api/clients'

interface UseProviderRuntimePreflightResult {
  providers: ProviderRuntimePreflightResponse[]
  loading: boolean
  error: string | null
  refresh: () => void
}

export function useProviderRuntimePreflight(ready: boolean = true): UseProviderRuntimePreflightResult {
  const [providers, setProviders] = useState<ProviderRuntimePreflightResponse[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [refreshToken, setRefreshToken] = useState(0)

  const refresh = useCallback(() => {
    setLoading(true)
    setRefreshToken((token) => token + 1)
  }, [])

  useEffect(() => {
    if (!ready) {
      return
    }

    let cancelled = false
    providerRuntimePreflightClient()
      .getProviderRuntimePreflight()
      .then((result) => {
        if (!cancelled) {
          setProviders(result)
          setError(null)
        }
      })
      .catch(() => {
        if (!cancelled) {
          setError('Provider runtime preflight is unavailable.')
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

  return { providers, loading, error, refresh }
}
