import { useCallback, useEffect, useLayoutEffect, useRef } from 'react'
import { ApiException } from '../../../api/generated/api-client'
import {
  captureGitWorkspaceCheckpointClient,
  gitCheckpointChangedFilesClient,
  gitCheckpointDiffClient,
  projectGitEvidenceClient,
} from '../../../api/clients'
import type {
  GetProjectGitEvidenceResponse,
  GitCheckpointChangedFileResponse,
} from '../../../api/clients'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'

const GENERIC_MESSAGE = 'This source evidence request could not be completed.'

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

// Files and diff belong to the exact checkpoint that was inspected, in addition to the project.
interface Inspection {
  checkpointId: string
  files: GitCheckpointChangedFileResponse[]
  diff: string
}

interface EvidenceFrame {
  evidence: GetProjectGitEvidenceResponse | null
  inspection: Inspection | null
  // The checkpoint whose inspection is in flight, and the one a failure belongs to (null: the
  // failure concerns the project's evidence as a whole).
  inspectingCheckpointId: string | null
  error: { message: string; checkpointId: string | null } | null
  loading: boolean
  capturing: boolean
}

function createFrame(projectId: string | null): EvidenceFrame {
  return { evidence: null, inspection: null, inspectingCheckpointId: null, error: null, loading: projectId !== null, capturing: false }
}

/** Keeps only bounded metadata and explicitly requested source text in component state. No
 * evidence is written to browser storage, URLs, or shared project summaries.
 *
 * Evidence, its errors and pending flags belong to the current project's lifetime (nothing is
 * owned while the project is null or evidence is disabled); files and diff additionally belong to
 * the exact checkpoint inspected, so a newer checkpoint neither retains nor accepts the previous
 * checkpoint's. Older overlapping reads and every continuation of a replaced lifetime are ignored. */
export function useProjectGitEvidence(projectId: string | null, enabled: boolean) {
  const owner = useOwnedLifetime(enabled ? projectId : null)
  const [frame, commit] = useOwnedState(owner, createFrame)
  const evidence = frame.evidence
  const checkpointId = evidence?.checkpointId

  // The checkpoint this render committed, for a handler retained from an earlier checkpoint.
  const committedCheckpointId = useRef<string | undefined>(undefined)
  useLayoutEffect(() => {
    committedCheckpointId.current = checkpointId
  }, [checkpointId])

  const refresh = useCallback(async () => {
    if (!projectId || !enabled || !owner.isActive()) {
      return
    }

    const isCurrent = owner.begin('read')
    commit((previous) => ({ ...previous, loading: true }))
    try {
      const response = await projectGitEvidenceClient().getProjectGitEvidence(projectId)
      if (isCurrent()) {
        commit((previous) => ({
          ...previous,
          evidence: response,
          inspection: previous.inspection?.checkpointId === response.checkpointId ? previous.inspection : null,
        }))
      }
    } catch (caught: unknown) {
      if (isCurrent()) {
        commit((previous) => ({ ...previous, error: { message: extractSafeErrorDetail(caught), checkpointId: null } }))
      }
    } finally {
      if (isCurrent()) {
        commit((previous) => ({ ...previous, loading: false }))
      }
    }
  }, [enabled, owner, projectId, commit])

  useEffect(() => {
    queueMicrotask(() => void refresh())
  }, [refresh])

  const capture = useCallback(async () => {
    if (!projectId || !enabled || !owner.isActive()) {
      return
    }

    const isCurrent = owner.begin('capture')
    commit((previous) => ({ ...previous, capturing: true, error: null, inspection: null, inspectingCheckpointId: null }))
    try {
      await captureGitWorkspaceCheckpointClient().captureGitWorkspaceCheckpoint(projectId)
      if (isCurrent()) {
        await refresh()
      }
    } catch (caught: unknown) {
      if (isCurrent()) {
        commit((previous) => ({ ...previous, error: { message: extractSafeErrorDetail(caught), checkpointId: null } }))
      }
    } finally {
      if (isCurrent()) {
        commit((previous) => ({ ...previous, capturing: false }))
      }
    }
  }, [enabled, owner, projectId, commit, refresh])

  const inspect = useCallback(async () => {
    if (!projectId || !checkpointId || !owner.isActive() || committedCheckpointId.current !== checkpointId) {
      return
    }

    const isCurrent = owner.begin('inspect')
    commit((previous) => ({ ...previous, inspectingCheckpointId: checkpointId, error: null }))
    try {
      const [files, diff] = await Promise.all([
        gitCheckpointChangedFilesClient().getGitCheckpointChangedFiles(projectId, checkpointId),
        gitCheckpointDiffClient().getGitCheckpointDiff(projectId, checkpointId),
      ])
      if (isCurrent()) {
        commit((previous) =>
          previous.evidence?.checkpointId === checkpointId
            ? { ...previous, inspection: { checkpointId, files, diff: diff.completeDiff ?? '' } }
            : previous,
        )
      }
    } catch (caught: unknown) {
      if (isCurrent()) {
        commit((previous) => ({
          ...previous,
          inspection: null,
          error: { message: extractSafeErrorDetail(caught), checkpointId },
        }))
      }
    } finally {
      if (isCurrent()) {
        commit((previous) => (previous.inspectingCheckpointId === checkpointId ? { ...previous, inspectingCheckpointId: null } : previous))
      }
    }
  }, [checkpointId, owner, projectId, commit])

  const inspection = frame.inspection?.checkpointId === checkpointId ? frame.inspection : null
  const error = frame.error && (frame.error.checkpointId === null || frame.error.checkpointId === checkpointId) ? frame.error.message : null

  return {
    evidence,
    changedFiles: inspection?.files ?? null,
    completeDiff: inspection?.diff ?? null,
    loading: frame.loading,
    capturing: frame.capturing,
    inspecting: checkpointId !== undefined && frame.inspectingCheckpointId === checkpointId,
    error,
    capture,
    inspect,
  }
}
