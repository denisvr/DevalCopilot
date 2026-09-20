import { useCallback, useRef, useState } from 'react'
import { ApiException, RequestReviewCorrectionRequest } from '../../../api/generated/api-client'
import { requestReviewCorrectionClient } from '../../../api/clients'

interface UseRequestReviewCorrectionResult {
  requesting: boolean
  error: string | null
  request: (runId: string, implementationReviewAttemptId: string) => Promise<boolean>
}

const GENERIC_MESSAGE = 'A review correction could not be requested for this run.'

function extractSafeErrorDetail(caught: unknown): string {
  if (!ApiException.isApiException(caught)) return GENERIC_MESSAGE
  try {
    const parsed = JSON.parse((caught as ApiException).response) as { errors?: { detail?: string }[] }
    const detail = parsed.errors?.[0]?.detail
    return typeof detail === 'string' && detail.length > 0 ? detail : GENERIC_MESSAGE
  } catch {
    return GENERIC_MESSAGE
  }
}

export function useRequestReviewCorrection(currentRunId: string, onRequested: () => void): UseRequestReviewCorrectionResult {
  const [state, setState] = useState({ runId: currentRunId, requesting: false, error: null as string | null })
  const generationRef = useRef(0)
  const currentRunIdRef = useRef(currentRunId)

  if (currentRunIdRef.current !== currentRunId) {
    currentRunIdRef.current = currentRunId
    generationRef.current += 1
  }

  const request = useCallback(async (runId: string, implementationReviewAttemptId: string) => {
    const generation = ++generationRef.current
    if (runId !== currentRunIdRef.current) return false
    setState({ runId, requesting: true, error: null })
    try {
      await requestReviewCorrectionClient().requestReviewCorrection(
        runId,
        new RequestReviewCorrectionRequest({ implementationReviewAttemptId }),
      )
      if (generation === generationRef.current && runId === currentRunIdRef.current) onRequested()
      return true
    } catch (caught: unknown) {
      if (generation === generationRef.current && runId === currentRunIdRef.current) {
        setState({ runId, requesting: false, error: extractSafeErrorDetail(caught) })
      }
      return false
    } finally {
      if (generation === generationRef.current && runId === currentRunIdRef.current) {
        setState((current) => ({ ...current, requesting: false }))
      }
    }
  }, [onRequested])

  return {
    requesting: state.runId === currentRunId ? state.requesting : false,
    error: state.runId === currentRunId ? state.error : null,
    request,
  }
}
