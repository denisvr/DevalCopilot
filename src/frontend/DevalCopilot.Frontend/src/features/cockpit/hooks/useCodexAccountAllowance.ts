import { useCallback, useEffect, useState } from 'react'
import type { CodexAccountAllowanceResponse } from '../../../api/clients'
import { codexAccountAllowanceClient } from '../../../api/clients'

interface UseCodexAccountAllowanceResult {
  allowance: CodexAccountAllowanceResponse | null
  loading: boolean
  error: string | null
  refresh: () => void
}

/**
 * Fetches the host-scoped Codex account-allowance snapshot once (never on a recurring interval —
 * this is an explicit, on-demand read, not scheduled polling). `refresh` lets a caller request a
 * fresh snapshot, e.g. from a manual "Refresh" control.
 */
export function useCodexAccountAllowance(ready: boolean = true): UseCodexAccountAllowanceResult {
  const [allowance, setAllowance] = useState<CodexAccountAllowanceResponse | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [refreshToken, setRefreshToken] = useState(0)

  const refresh = useCallback(() => {
    setAllowance(null)
    setError(null)
    setLoading(true)
    setRefreshToken((token) => token + 1)
  }, [])

  useEffect(() => {
    if (!ready) {
      return
    }

    let cancelled = false
    codexAccountAllowanceClient()
      .getCodexAccountAllowance()
      .then((result) => {
        if (!cancelled) {
          setAllowance(result)
          setError(null)
        }
      })
      .catch(() => {
        if (!cancelled) {
          // A failed refresh must never leave a previously observed status and retrieval time
          // on screen looking current — that would misrepresent stale data as a fresh read.
          setAllowance(null)
          setError('Codex account allowance is unavailable.')
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

  return { allowance, loading, error, refresh }
}
