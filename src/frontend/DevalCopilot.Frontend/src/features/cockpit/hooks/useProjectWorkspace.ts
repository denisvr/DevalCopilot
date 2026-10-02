import { useCallback, useEffect } from 'react'
import { ApiException } from '../../../api/generated/api-client'
import { prepareWorkspaceClient, projectWorkspaceClient, recheckPhysicalIdentityClient } from '../../../api/clients'
import type { GetProjectWorkspaceResponse } from '../../../api/clients'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'

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

interface WorkspaceFrame {
  workspace: GetProjectWorkspaceResponse | null
  loading: boolean
  preparing: boolean
  rechecking: boolean
  error: string | null
}

function createFrame(projectId: string | null): WorkspaceFrame {
  return { workspace: null, loading: projectId !== null, preparing: false, rechecking: false, error: null }
}

/** Loads a project's current candidate-workspace state and exposes the two bounded, explicit
 * actions available on it: requesting preparation, and rechecking physical identity when it is
 * blocking preparation. Neither action ever retries automatically.
 *
 * Everything exposed belongs to the current project's lifetime: another project, none, or a
 * return to an earlier one starts from an empty loading frame, and a read, action or handler of
 * a replaced lifetime can no longer change state, refresh, or start work. An already accepted
 * server operation is neither undone nor retried. */
export function useProjectWorkspace(projectId: string | null): UseProjectWorkspaceResult {
  const owner = useOwnedLifetime(projectId)
  const [frame, commit] = useOwnedState(owner, createFrame)

  const refresh = useCallback(async () => {
    if (!projectId || !owner.isActive()) {
      return
    }

    const isCurrent = owner.begin('read')
    commit((previous) => ({ ...previous, loading: true }))
    try {
      const response = await projectWorkspaceClient().getProjectWorkspace(projectId)
      if (isCurrent()) {
        commit((previous) => ({ ...previous, workspace: response }))
      }
    } catch (caught: unknown) {
      if (isCurrent()) {
        commit((previous) => ({ ...previous, error: extractSafeErrorDetail(caught) }))
      }
    } finally {
      if (isCurrent()) {
        commit((previous) => ({ ...previous, loading: false }))
      }
    }
  }, [owner, projectId, commit])

  useEffect(() => {
    void refresh()
  }, [refresh])

  const prepare = useCallback(async () => {
    if (!projectId || !owner.isActive()) {
      return
    }

    const isCurrent = owner.begin('prepare')
    commit((previous) => ({ ...previous, preparing: true, error: null }))
    try {
      await prepareWorkspaceClient().prepareRepositoryWorkspace(projectId)
      if (isCurrent()) {
        await refresh()
      }
    } catch (caught: unknown) {
      if (isCurrent()) {
        commit((previous) => ({ ...previous, error: extractSafeErrorDetail(caught) }))
      }
    } finally {
      if (isCurrent()) {
        commit((previous) => ({ ...previous, preparing: false }))
      }
    }
  }, [owner, projectId, commit, refresh])

  const recheckIdentity = useCallback(async () => {
    if (!projectId || !owner.isActive()) {
      return
    }

    const isCurrent = owner.begin('recheck')
    commit((previous) => ({ ...previous, rechecking: true, error: null }))
    try {
      await recheckPhysicalIdentityClient().recheckProjectPhysicalIdentity(projectId)
      if (isCurrent()) {
        await refresh()
      }
    } catch (caught: unknown) {
      if (isCurrent()) {
        commit((previous) => ({ ...previous, error: extractSafeErrorDetail(caught) }))
      }
    } finally {
      if (isCurrent()) {
        commit((previous) => ({ ...previous, rechecking: false }))
      }
    }
  }, [owner, projectId, commit, refresh])

  return { ...frame, prepare, recheckIdentity }
}
