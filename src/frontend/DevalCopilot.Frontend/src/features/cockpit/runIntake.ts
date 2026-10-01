import { ApiException } from '../../api/generated/api-client'

/** The server's objective boundary; the client guard matches it and never trims what is sent. */
export const MAX_OBJECTIVE_LENGTH = 2000

export const RUN_INTAKE_BLOCKED_REASON = 'A new objective can be recorded only after every run of this project has finished.'

/** Null when the objective is acceptable; otherwise a fixed, safe message. */
export function validateRunObjective(objective: string): string | null {
  if (objective.trim().length === 0) {
    return 'Enter an objective to record a run.'
  }
  if (objective.length > MAX_OBJECTIVE_LENGTH) {
    return `The objective must be at most ${MAX_OBJECTIVE_LENGTH} characters.`
  }
  return null
}

function failureCode(caught: unknown): string | null {
  if (!ApiException.isApiException(caught)) {
    return null
  }
  try {
    const parsed = JSON.parse((caught as ApiException).response) as { errors?: { code?: unknown }[] }
    const code = parsed.errors?.[0]?.code
    return typeof code === 'string' ? code : null
  } catch {
    return null
  }
}

export function isRunIntakeBlocked(caught: unknown): boolean {
  return failureCode(caught) === 'runs.intent_blocked'
}

/** Maps a failure to fixed, safe copy; raw server or exception text is never displayed. */
export function describeRunIntakeFailure(caught: unknown, fallback: string): string {
  const code = failureCode(caught)
  if (code === 'runs.intent_blocked') {
    return 'This project already has an unfinished run, so no new run was created. The project list is being refreshed.'
  }
  if (code === 'runs.intent_conflict') {
    return 'Another request for this project was being processed at the same time. No run was created; try again.'
  }
  if (code === 'projects.not_found') {
    return 'This project could not be found.'
  }
  if (ApiException.isApiException(caught) && (caught as ApiException).status === 400) {
    return `The objective was not accepted. It must contain text and be at most ${MAX_OBJECTIVE_LENGTH} characters.`
  }
  return fallback
}
