import { useCallback, useState } from 'react'
import { ApiException, RegisterProjectRequest } from '../../../api/generated/api-client'
import { registerProjectClient } from '../../../api/clients'

interface UseRegisterProjectResult {
  registering: boolean
  error: string | null
  register: (name: string, path: string) => Promise<boolean>
}

const GENERIC_MESSAGE = 'This project could not be registered.'

/**
 * Extracts only the fixed, safe error text the backend itself wrote (an `ApiError.detail`
 * value from the structured problem-details body) — never a raw exception message, stack
 * trace, or anything else the transport layer might have captured.
 */
function extractSafeErrorDetail(caught: unknown): string {
  if (!ApiException.isApiException(caught)) {
    return GENERIC_MESSAGE
  }

  try {
    const parsed = JSON.parse((caught as ApiException).response) as { errors?: { detail?: string }[] }
    const detail = parsed.errors?.[0]?.detail
    return typeof detail === 'string' && detail.length > 0 ? detail : GENERIC_MESSAGE
  } catch {
    return GENERIC_MESSAGE
  }
}

/** Registers a project, then triggers the caller's own refresh — the same
 * refresh-after-mutate pattern already used by `useHostCapabilityRefresh`. */
export function useRegisterProject(onRegistered: () => void): UseRegisterProjectResult {
  const [registering, setRegistering] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const register = useCallback(
    async (name: string, path: string) => {
      setRegistering(true)
      setError(null)
      try {
        await registerProjectClient().registerProject(new RegisterProjectRequest({ name, path }))
        onRegistered()
        return true
      } catch (caught: unknown) {
        setError(extractSafeErrorDetail(caught))
        return false
      } finally {
        setRegistering(false)
      }
    },
    [onRegistered],
  )

  return { registering, error, register }
}
