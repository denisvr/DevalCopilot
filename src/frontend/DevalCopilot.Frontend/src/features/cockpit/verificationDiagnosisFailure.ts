import { ApiException } from '../../api/generated/api-client'

/** Fixed local copy per stable problem code; the server's own wording is not shown for these. */
const MESSAGES: Record<string, string> = {
  'runs.not_found': 'This run was not found, so nothing was requested.',
  'runs.not_running': 'This run is not running, so nothing was requested.',
  'attempts.run_has_active_attempt': 'Another Agent attempt is still active for this run. Nothing was requested.',
  'agent_attempts.budget_exhausted': 'The run’s Agent attempt budget is exhausted, so nothing was requested.',
  'agent_attempts.time_budget_exceeded': 'The run’s reserved Agent time does not fit this request, so nothing was requested.',
  'agent_attempts.time_budget_evidence_invalid':
    'The run’s Agent time-budget evidence could not be validated, so nothing was requested.',
  'agent_attempts.budget_slot_conflict': 'The run’s Agent attempt slot changed while requesting. Refresh and try again.',
  'agent_attempts.workspace_not_ready': 'The run’s workspace is not ready for an Agent attempt, so nothing was requested.',
  'agent_attempts.lease_not_active': 'The run no longer holds an active workspace lease, so nothing was requested.',
  'agent_attempts.checkpoint_missing': 'The run has no source checkpoint to work from, so nothing was requested.',
  'agent_attempts.checkpoint_not_current':
    'The source checkpoint is no longer current. Refresh, run verification again if needed, and then try again.',
  'agent_attempts.provider_not_observed':
    'The required provider runtime is not currently observed as available, so nothing was requested.',
  'agent_attempts.token_stop_reached':
    'A configured token stop has been reached for this provider, so nothing was requested.',
  'agent_attempts.token_stop_evidence_indeterminate':
    'Token usage evidence for this provider could not be determined, so nothing was requested.',
  'agent_attempts.account_usage_stop_reached': 'A configured Codex account-usage stop was reached, so nothing was started.',
  'agent_attempts.account_usage_stop_evidence_unavailable':
    'The configured Codex account-usage stop could not be checked, so nothing was started.',
  'agent_attempts.account_usage_stop_setting_invalid':
    'The saved Codex account-usage stop is not a valid setting. Set or clear it and try again.',
  'agent_attempts.account_usage_stop_policy_changed': 'The Codex account-usage stop changed while requesting. Refresh and try again.',
  'agent_attempts.token_stop_policy_changed':
    'The token-stop policy changed while requesting. Refresh and try again.',
  'agent_attempts.execution_report_not_found': 'That implementation report was not found, so nothing was requested.',
  'agent_attempts.not_provider_observed_execution_report':
    'That report is not a provider-observed implementation report, so nothing was requested.',
  'agent_attempts.result_checkpoint_mismatch':
    'That report does not match the current source checkpoint, so nothing was requested.',
  'agent_attempts.implementer_attempt_not_valid':
    'The implementation attempt behind that report could not be validated, so nothing was requested.',
  'agent_attempts.already_diagnosed':
    'The failed verification of this implementation was already diagnosed. Run verification again before requesting a new diagnosis.',
  'agent_attempts.already_corrected': 'The diagnosed findings were already corrected, so nothing was requested.',
  'agent_attempts.diagnosis_not_applicable':
    'This diagnosis no longer applies to the current source, so its findings cannot be corrected. Nothing was requested.',
  'agent_attempts.context_manifest_too_large':
    'The evidence for this request is too large to send to the provider, so nothing was requested.',
  'agent_attempts.assignment_preference_changed':
    'The provider assignment preference changed while requesting. Refresh and try again.',
  'attempts.persistence_failed': 'The host could not record the request, so nothing was requested.',
  'attempts.persistence_unresolved':
    'The host could not confirm whether the request was recorded. Refresh the run before trying again.',
  'verification_diagnosis.workspace_not_ready': 'The run’s workspace is not ready, so a diagnosis cannot be requested.',
  'verification_diagnosis.no_current_execution_report':
    'There is no current implementation report to diagnose, so a diagnosis cannot be requested.',
  'verification_diagnosis.no_verification_commands_enabled':
    'No verification command is enabled, so there is no failed verification to diagnose.',
  'verification_diagnosis.evidence_missing':
    'Verification has not been run for the current source, so there is nothing to diagnose yet.',
  'verification_diagnosis.evidence_running': 'Verification is still running. Wait for it to finish before requesting a diagnosis.',
  'verification_diagnosis.evidence_not_diagnosable':
    'The current verification evidence cannot be diagnosed. Run verification again and then try again.',
  'verification_diagnosis.no_failed_verification':
    'No enabled verification command failed for the current source, so there is nothing to diagnose. Request the ordinary code review instead.',
  'verification_diagnosis.failed_output_unavailable':
    'The output of the failed verification is unavailable, so it cannot be diagnosed. Run verification again.',
}

/**
 * Maps a failed diagnosis or diagnosis-correction request to safe copy. A known problem code gets
 * fixed local copy; any other problem falls back to the backend-authored detail the sibling
 * Agent actions already show for the shared run-wide stops, and otherwise to `generic`. Raw
 * exception text is never shown.
 */
export function describeVerificationDiagnosisFailure(caught: unknown, generic: string): string {
  if (!ApiException.isApiException(caught)) {
    return generic
  }

  try {
    const parsed = JSON.parse((caught as ApiException).response) as { errors?: { code?: unknown; detail?: unknown }[] }
    const code = parsed.errors?.[0]?.code
    if (typeof code === 'string' && Object.hasOwn(MESSAGES, code)) {
      return MESSAGES[code]
    }
    const detail = parsed.errors?.[0]?.detail
    return typeof detail === 'string' && detail.length > 0 ? detail : generic
  } catch {
    return generic
  }
}
