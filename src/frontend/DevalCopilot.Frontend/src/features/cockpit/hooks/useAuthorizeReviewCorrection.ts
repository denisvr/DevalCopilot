import { useCallback, useRef, useState } from 'react'
import { ApiException } from '../../../api/generated/api-client'
import { authorizeReviewCorrectionClient } from '../../../api/clients'

interface UseAuthorizeReviewCorrectionResult {
  authorizing: boolean
  error: string | null
  authorize: (runId: string, escalationId: string) => Promise<boolean>
}

const GENERIC_MESSAGE = 'An additional correction could not be authorized for this run.'

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

export function useAuthorizeReviewCorrection(currentRunId: string, onAuthorized: () => void): UseAuthorizeReviewCorrectionResult {
  const [state, setState] = useState({ runId: currentRunId, authorizing: false, error: null as string | null })
  const generationRef = useRef(0)
  const currentRunIdRef = useRef(currentRunId)

  if (currentRunIdRef.current !== currentRunId) {
    currentRunIdRef.current = currentRunId
    generationRef.current += 1
  }

  const authorize = useCallback(async (runId: string, escalationId: string) => {
    const generation = ++generationRef.current
    if (runId !== currentRunIdRef.current) return false
    setState({ runId, authorizing: true, error: null })
    try {
      await authorizeReviewCorrectionClient().authorizeReviewCorrection(runId, escalationId)
      if (generation === generationRef.current && runId === currentRunIdRef.current) onAuthorized()
      return true
    } catch (caught: unknown) {
      if (generation === generationRef.current && runId === currentRunIdRef.current) {
        setState({ runId, authorizing: false, error: extractSafeErrorDetail(caught) })
      }
      return false
    } finally {
      if (generation === generationRef.current && runId === currentRunIdRef.current) {
        setState((current) => ({ ...current, authorizing: false }))
      }
    }
  }, [onAuthorized])

  return {
    authorizing: state.runId === currentRunId ? state.authorizing : false,
    error: state.runId === currentRunId ? state.error : null,
    authorize,
  }
}
