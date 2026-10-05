import { useCallback } from 'react'
import { ApiException, SetCodexAccountUsageStopRequest } from '../../../api/generated/api-client'
import { setCodexAccountUsageStopClient } from '../../../api/clients'
import { useRunScopedAction } from './useRunScopedAction'
import { parseCodexAccountUsageStopDraft } from '../describeCodexAccountUsageStop'

interface UseSetCodexAccountUsageStopResult {
  saving: boolean
  error: string | null
  /** Saves a draft (empty string clears). Resolves true only when the server accepted it. */
  save: (draft: string) => Promise<boolean>
  /** Clears the run's stop by sending an explicit null. */
  clear: () => Promise<boolean>
}

export const ACCOUNT_USAGE_STOP_VALIDATION_MESSAGE = 'Enter a whole number from 1 to 100.'
const GENERIC_MESSAGE = 'The Codex account-usage stop could not be saved for this run.'

/** Fixed, safe messages chosen by HTTP status only; server text is never displayed. */
function safeMessage(caught: unknown): string {
  if (!ApiException.isApiException(caught)) {
    return GENERIC_MESSAGE
  }
  switch ((caught as ApiException).status) {
    case 400:
      return 'The account-usage stop must be a whole number from 1 to 100.'
    case 404:
      return 'This run could not be found.'
    case 409:
      return 'The account-usage stop changed concurrently, or this run does not allow it. Reload the run and retry.'
    case 422:
      return "This run's Codex account-usage stop can no longer be changed."
    default:
      return GENERIC_MESSAGE
  }
}

/**
 * Sets or clears the run-scoped Codex account-usage stop (a used-percent threshold, 1..100) that
 * the backend enforces on new Codex claims. It is a local guard over a provider-reported
 * percentage. Changing it never alters the threshold an already-claimed attempt recorded, but a
 * claimed attempt is still checked before it starts and can be stopped. Clearing sends an explicit
 * JSON null because the server rejects a missing member. The whole interaction (request guard,
 * pending state, errors) is owned by the committed run plus `settingIdentity`, the identity of the
 * authoritative setting the caller shows: a change of either ends the owner, and returning to an
 * earlier identity is a new owner (see `useRunScopedAction`). A callback retained from an ended
 * owner is inert, an obsolete completion never touches a replacement owner (the server request
 * itself still happened), a second submission in flight on one owner is ignored, and a resolved
 * false never authorizes a caller to refresh or show success.
 */
export function useSetCodexAccountUsageStop(runId: string, settingIdentity: string): UseSetCodexAccountUsageStopResult {
  const ownerKey = JSON.stringify([runId, settingIdentity])
  const { busy, error, run, reportError } = useRunScopedAction(ownerKey)

  const send = useCallback(
    (percent: number | null) =>
      run(
        ownerKey,
        // fromJS keeps a null member, so the serialized body is {'percent':null} for a clear.
        () => setCodexAccountUsageStopClient().setCodexAccountUsageStop(runId, SetCodexAccountUsageStopRequest.fromJS({ percent })),
        { toMessage: safeMessage },
      ),
    [run, runId, ownerKey],
  )

  const save = useCallback(
    async (draft: string) => {
      const parsed = parseCodexAccountUsageStopDraft(draft)
      if (parsed.kind === 'invalid') {
        reportError(ACCOUNT_USAGE_STOP_VALIDATION_MESSAGE)
        return false
      }
      return send(parsed.kind === 'set' ? parsed.percent : null)
    },
    [reportError, send],
  )

  const clear = useCallback(() => send(null), [send])

  return { saving: busy, error, save, clear }
}
