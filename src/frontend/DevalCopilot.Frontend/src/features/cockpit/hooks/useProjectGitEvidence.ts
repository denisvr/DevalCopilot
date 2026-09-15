import { useCallback, useEffect, useState } from 'react'
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

/** Keeps only bounded metadata and explicitly requested source text in component state. No
 * evidence is written to browser storage, URLs, or shared project summaries. */
export function useProjectGitEvidence(projectId: string | null, enabled: boolean) {
  const [evidence, setEvidence] = useState<GetProjectGitEvidenceResponse | null>(null)
  const [changedFiles, setChangedFiles] = useState<GitCheckpointChangedFileResponse[] | null>(null)
  const [completeDiff, setCompleteDiff] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [capturing, setCapturing] = useState(false)
  const [inspecting, setInspecting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    if (!projectId || !enabled) {
      setEvidence(null)
      setChangedFiles(null)
      setCompleteDiff(null)
      return
    }

    setLoading(true)
    try {
      setEvidence(await projectGitEvidenceClient().getProjectGitEvidence(projectId))
    } catch (caught: unknown) {
      setError(extractSafeErrorDetail(caught))
    } finally {
      setLoading(false)
    }
  }, [enabled, projectId])

  useEffect(() => {
    queueMicrotask(() => void refresh())
  }, [refresh])

  const checkpointId = evidence?.checkpointId

  const capture = useCallback(async () => {
    if (!projectId || !enabled) {
      return
    }

    setCapturing(true)
    setError(null)
    setChangedFiles(null)
    setCompleteDiff(null)
    try {
      await captureGitWorkspaceCheckpointClient().captureGitWorkspaceCheckpoint(projectId)
      await refresh()
    } catch (caught: unknown) {
      setError(extractSafeErrorDetail(caught))
    } finally {
      setCapturing(false)
    }
  }, [enabled, projectId, refresh])

  const inspect = useCallback(async () => {
    if (!projectId || !checkpointId) {
      return
    }

    setInspecting(true)
    setError(null)
    try {
      const [files, diff] = await Promise.all([
        gitCheckpointChangedFilesClient().getGitCheckpointChangedFiles(projectId, checkpointId),
        gitCheckpointDiffClient().getGitCheckpointDiff(projectId, checkpointId),
      ])
      setChangedFiles(files)
      setCompleteDiff(diff.completeDiff ?? '')
    } catch (caught: unknown) {
      setError(extractSafeErrorDetail(caught))
      setChangedFiles(null)
      setCompleteDiff(null)
    } finally {
      setInspecting(false)
    }
  }, [checkpointId, projectId])

  return { evidence, changedFiles, completeDiff, loading, capturing, inspecting, error, capture, inspect }
}
