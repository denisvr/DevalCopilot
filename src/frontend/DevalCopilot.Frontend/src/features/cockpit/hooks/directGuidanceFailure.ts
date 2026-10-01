import { ApiException } from '../../../api/generated/api-client'

const INVALID_CODE = 'agent_attempts.direct_guidance_invalid'
const UNAVAILABLE_CODE = 'agent_attempts.direct_guidance_unavailable'

/**
 * Fixed copy for the two direct-guidance refusals. The submitted text is never echoed, and neither is
 * the server's own wording: only the stable problem code selects the message. Null for any other failure.
 */
export function describeDirectGuidanceFailure(caught: unknown): string | null {
  if (!ApiException.isApiException(caught)) {
    return null
  }
  try {
    const parsed = JSON.parse((caught as ApiException).response) as { errors?: { code?: unknown }[] }
    const code = parsed.errors?.[0]?.code
    if (code === INVALID_CODE) {
      return 'The guidance was not accepted. It must be non-blank text of at most 600 characters without control characters.'
    }
    if (code === UNAVAILABLE_CODE) {
      return 'Direct guidance is available only within the ordinary correction budget, so this request was not accepted. Nothing was created or consumed.'
    }
    return null
  } catch {
    return null
  }
}
