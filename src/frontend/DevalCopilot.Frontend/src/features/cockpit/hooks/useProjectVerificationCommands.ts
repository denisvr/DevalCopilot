import { useCallback, useEffect } from 'react'
import { ApiException, ConfigureVerificationCommandRequest, UpdateVerificationCommandRequest } from '../../../api/generated/api-client'
import {
  configureVerificationCommandClient,
  deleteVerificationCommandClient,
  projectVerificationCommandsClient,
  updateVerificationCommandClient,
} from '../../../api/clients'
import type { VerificationCommandResponse } from '../../../api/clients'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'

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
interface CommandsFrame {
  commands: VerificationCommandResponse[]
  loading: boolean
  saving: boolean
  error: string | null
}

function createFrame(projectId: string | null): CommandsFrame {
  return { commands: [], loading: projectId !== null, saving: false, error: null }
}

/** Keeps command recipes in the API/database only. It never treats text as a shell command:
 * arguments remain a literal array all the way to the generated MVC client.
 *
 * The list, errors and pending flag belong to the current project's lifetime. A configure, update
 * or remove that the server already accepted stays real when the project changes meanwhile, but its
 * continuation then neither refreshes, reports, nor resolves true for the replacement. */
export function useProjectVerificationCommands(projectId: string | null) {
  const owner = useOwnedLifetime(projectId)
  const [frame, commit] = useOwnedState(owner, createFrame)

  const refresh = useCallback(async () => {
    if (!projectId || !owner.isActive()) {
      return
    }

    const isCurrent = owner.begin('read')
    commit((previous) => ({ ...previous, loading: true }))
    try {
      const next = await projectVerificationCommandsClient().getProjectVerificationCommands(projectId)
      if (isCurrent()) {
        commit((previous) => ({ ...previous, commands: next }))
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
    queueMicrotask(() => void refresh())
  }, [refresh])

  // Runs one write bound to this project's lifetime. Resolves true only when the server accepted it
  // and the lifetime is still the current, newest write of it.
  const save = useCallback(
    async (execute: (id: string) => Promise<unknown>) => {
      if (!projectId || !owner.isActive()) {
        return false
      }

      const isCurrent = owner.begin('save')
      commit((previous) => ({ ...previous, saving: true, error: null }))
      try {
        await execute(projectId)
        if (!isCurrent()) {
          return false
        }
        await refresh()
        return isCurrent()
      } catch (caught: unknown) {
        if (isCurrent()) {
          commit((previous) => ({ ...previous, error: extractSafeErrorDetail(caught) }))
        }
        return false
      } finally {
        if (isCurrent()) {
          commit((previous) => ({ ...previous, saving: false }))
        }
      }
    },
    [owner, projectId, commit, refresh],
  )

  const configure = useCallback(
    (draft: VerificationCommandDraft) =>
      save((id) =>
        configureVerificationCommandClient().configureVerificationCommand(id, new ConfigureVerificationCommandRequest(draft)),
      ),
    [save],
  )

  const update = useCallback(
    async (command: VerificationCommandResponse, isEnabled: boolean) => {
      const verificationCommandId = command.verificationCommandId
      if (!verificationCommandId) {
        return
      }

      await save((id) =>
        updateVerificationCommandClient().updateVerificationCommand(
          id,
          verificationCommandId,
          new UpdateVerificationCommandRequest({
            name: command.name ?? '',
            executablePath: command.executablePath ?? '',
            arguments: command.arguments ?? [],
            timeoutSeconds: command.timeoutSeconds ?? 60,
            isEnabled,
          }),
        ),
      )
    },
    [save],
  )

  const remove = useCallback(
    async (verificationCommandId: string) => {
      await save((id) => deleteVerificationCommandClient().deleteVerificationCommand(id, verificationCommandId))
    },
    [save],
  )

  return { ...frame, configure, update, remove }
}
