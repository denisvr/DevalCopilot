import { useCallback, useEffect, useState } from 'react'
import { ApiException, ConfigureVerificationCommandRequest, UpdateVerificationCommandRequest } from '../../../api/generated/api-client'
import {
  configureVerificationCommandClient,
  deleteVerificationCommandClient,
  projectVerificationCommandsClient,
  updateVerificationCommandClient,
} from '../../../api/clients'
import type { VerificationCommandResponse } from '../../../api/clients'

const GENERIC_MESSAGE = 'This verification configuration request could not be completed.'

function extractSafeErrorDetail(caught: unknown): string {
  if (!ApiException.isApiException(caught)) {
    return GENERIC_MESSAGE
  }

  try {
    const parsed = JSON.parse(caught.response) as { errors?: { detail?: string }[] }
    const detail = parsed.errors?.[0]?.detail
    return typeof detail === 'string' && detail.length > 0 ? detail : GENERIC_MESSAGE
  } catch {
    return GENERIC_MESSAGE
  }
}

export interface VerificationCommandDraft {
  name: string
  executablePath: string
  arguments: string[]
  timeoutSeconds: number
  isEnabled: boolean
}

/** Keeps command recipes in the API/database only. It never treats text as a shell command:
 * arguments remain a literal array all the way to the generated MVC client. */
export function useProjectVerificationCommands(projectId: string | null) {
  const [commands, setCommands] = useState<VerificationCommandResponse[]>([])
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    if (!projectId) {
      setCommands([])
      return
    }

    setLoading(true)
    try {
      setCommands(await projectVerificationCommandsClient().getProjectVerificationCommands(projectId))
    } catch (caught: unknown) {
      setError(extractSafeErrorDetail(caught))
    } finally {
      setLoading(false)
    }
  }, [projectId])

  useEffect(() => {
    queueMicrotask(() => void refresh())
  }, [refresh])

  const configure = useCallback(async (draft: VerificationCommandDraft) => {
    if (!projectId) {
      return false
    }

    setSaving(true)
    setError(null)
    try {
      await configureVerificationCommandClient().configureVerificationCommand(
        projectId,
        new ConfigureVerificationCommandRequest(draft),
      )
      await refresh()
      return true
    } catch (caught: unknown) {
      setError(extractSafeErrorDetail(caught))
      return false
    } finally {
      setSaving(false)
    }
  }, [projectId, refresh])

  const update = useCallback(async (command: VerificationCommandResponse, isEnabled: boolean) => {
    if (!projectId || !command.verificationCommandId) {
      return
    }

    setSaving(true)
    setError(null)
    try {
      await updateVerificationCommandClient().updateVerificationCommand(
        projectId,
        command.verificationCommandId,
        new UpdateVerificationCommandRequest({
          name: command.name ?? '',
          executablePath: command.executablePath ?? '',
          arguments: command.arguments ?? [],
          timeoutSeconds: command.timeoutSeconds ?? 60,
          isEnabled,
        }),
      )
      await refresh()
    } catch (caught: unknown) {
      setError(extractSafeErrorDetail(caught))
    } finally {
      setSaving(false)
    }
  }, [projectId, refresh])

  const remove = useCallback(async (verificationCommandId: string) => {
    if (!projectId) {
      return
    }

    setSaving(true)
    setError(null)
    try {
      await deleteVerificationCommandClient().deleteVerificationCommand(projectId, verificationCommandId)
      await refresh()
    } catch (caught: unknown) {
      setError(extractSafeErrorDetail(caught))
    } finally {
      setSaving(false)
    }
  }, [projectId, refresh])

  return { commands, loading, saving, error, configure, update, remove }
}
