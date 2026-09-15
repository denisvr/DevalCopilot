import { useCallback, useEffect, useState } from 'react'
import { ApiException } from '../../../api/generated/api-client'
import { prepareWorkspaceClient, projectWorkspaceClient, recheckPhysicalIdentityClient } from '../../../api/clients'
import type { GetProjectWorkspaceResponse } from '../../../api/clients'

interface UseProjectWorkspaceResult {
  workspace: GetProjectWorkspaceResponse | null
  loading: boolean
  preparing: boolean
  rechecking: boolean
  error: string | null
  prepare: () => Promise<void>
  recheckIdentity: () => Promise<void>
}

const GENERIC_MESSAGE = 'This workspace request could not be completed.'

/** Extracts only the fixed, safe error text the backend itself wrote — never a raw exception
 * message, stack trace, or anything else the transport layer might have captured. The same
 * pattern already used by `useRegisterProject`. */
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

/** Loads a project's current candidate-workspace state and exposes the two bounded, explicit
 * actions available on it: requesting preparation, and rechecking physical identity when it is
 * blocking preparation. Neither action ever retries automatically. */
export function useProjectWorkspace(projectId: string | null): UseProjectWorkspaceResult {
  const [workspace, setWorkspace] = useState<GetProjectWorkspaceResponse | null>(null)
  const [loading, setLoading] = useState(false)
  const [preparing, setPreparing] = useState(false)
  const [rechecking, setRechecking] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    if (!projectId) {
      setWorkspace(null)
      return
    }

    setLoading(true)
    try {
      const response = await projectWorkspaceClient().getProjectWorkspace(projectId)
      setWorkspace(response)
    } catch (caught: unknown) {
      setError(extractSafeErrorDetail(caught))
    } finally {
      setLoading(false)
    }
  }, [projectId])

  useEffect(() => {
    void refresh()
  }, [refresh])

  const prepare = useCallback(async () => {
    if (!projectId) {
      return
    }

    setPreparing(true)
    setError(null)
    try {
      await prepareWorkspaceClient().prepareRepositoryWorkspace(projectId)
      await refresh()
    } catch (caught: unknown) {
      setError(extractSafeErrorDetail(caught))
    } finally {
      setPreparing(false)
    }
  }, [projectId, refresh])

  const recheckIdentity = useCallback(async () => {
    if (!projectId) {
      return
    }

    setRechecking(true)
    setError(null)
    try {
      await recheckPhysicalIdentityClient().recheckProjectPhysicalIdentity(projectId)
      await refresh()
    } catch (caught: unknown) {
      setError(extractSafeErrorDetail(caught))
    } finally {
      setRechecking(false)
    }
  }, [projectId, refresh])

  return { workspace, loading, preparing, rechecking, error, prepare, recheckIdentity }
}
