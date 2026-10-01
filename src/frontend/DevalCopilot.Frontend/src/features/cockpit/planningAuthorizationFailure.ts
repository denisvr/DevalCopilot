import { ApiException } from '../../api/generated/api-client'

const GENERIC_MESSAGE = 'The authorization could not be recorded for this escalation. Nothing was claimed or spent.'

/** Fixed local copy per stable problem code; neither the submitted rationale nor the server's own wording is shown. */
const MESSAGES: Record<string, string> = {
  'planning_authorizations.rationale_invalid':
    'The reason was not accepted. It must be non-blank text of at most 600 characters without control characters.',
  'planning_authorizations.rationale_conflict':
    'An authorization with a different reason already exists for this escalation. It is not changed or renewed.',
  'planning_authorizations.already_consumed':
    'The authorization for this escalation was already used by an implementation claim. It is not renewed.',
  'planning_authorizations.source_stale':
    'This escalation no longer belongs to the run’s current plan and checkpoint, so it cannot be authorized.',
  'planning_authorizations.source_invalid':
    'The escalation and its plan lineage could not be validated, so nothing was authorized.',
  'planning_authorizations.source_not_found': 'This escalation was not found for the run, so nothing was authorized.',
  'planning_authorizations.context_not_current':
    'The run, its workspace, lease, or checkpoint no longer permit this authorization right now.',
  'planning_authorizations.recorded_invalid':
    'The recorded authorization could not be validated, so it is neither shown as usable nor changed.',
}

/** Maps a failed authorization request to fixed, safe copy; never raw exception text or echoed input. */
export function describePlanningAuthorizationFailure(caught: unknown): string {
  if (!ApiException.isApiException(caught)) {
    return GENERIC_MESSAGE
  }

  try {
    const parsed = JSON.parse((caught as ApiException).response) as { errors?: { code?: unknown }[] }
    const code = parsed.errors?.[0]?.code
    return typeof code === 'string' && Object.hasOwn(MESSAGES, code) ? MESSAGES[code] : GENERIC_MESSAGE
  } catch {
    return GENERIC_MESSAGE
  }
}
