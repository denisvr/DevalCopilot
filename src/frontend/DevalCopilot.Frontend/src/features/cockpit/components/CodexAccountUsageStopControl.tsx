import { useState } from 'react'
import type { CodexAccountUsageStopResponse } from '../../../api/clients'
import { describeRunAccountUsageStop } from '../describeCodexAccountUsageStop'
import { useOwnedFlow } from '../hooks/useRunActionLifetime'
import { useSetCodexAccountUsageStop } from '../hooks/useSetCodexAccountUsageStop'

const SYNC_FAILURE_MESSAGE = 'Saved, but the cockpit could not be refreshed; the displayed setting may be out of date.'
const EMPTY_SAVE_MESSAGE = 'Enter a whole number from 1 to 100, or use Clear.'
const NOTE =
  'Stops a new Codex request when a reported account-usage window reaches this percentage. It is checked when the request is claimed and again just before it starts. Changing the setting never alters the threshold an already-claimed attempt recorded, but a claimed attempt is still checked before it starts and can be stopped. It is a local guard over a provider-reported percentage, not account access, remaining quota or live capacity.'

interface CodexAccountUsageStopControlProps {
  runId: string
  /** The run's current saved stop from the authoritative cockpit projection. */
  setting: CodexAccountUsageStopResponse | null | undefined
  /** False once the run lifecycle no longer allows changes; the setting is then shown read-only. */
  editable: boolean
  /** Re-queries the authoritative cockpit; resolves false on failure or if the run changed meanwhile. */
  onSaved?: () => Promise<boolean>
}

/**
 * Lets the owner set an optional used-percent threshold (1..100) for this run's NEW Codex claims.
 * It is a local guard over a provider-reported percentage, independent of the displayed allowance
 * observation, which never gates or enables anything here. The value shown is always the cockpit's
 * saved setting; drafts, pending state and messages are owned by the run plus that authoritative
 * setting, so a stale completion never writes or refreshes a replacement selection.
 */
export function CodexAccountUsageStopControl({ runId, setting, editable, onSaved }: CodexAccountUsageStopControlProps) {
  const authoritativeDraft = setting?.state === 'Configured' && setting.percent !== undefined ? String(setting.percent) : ''
  const identity = `${runId}|${setting?.state ?? ''}|${setting?.percent ?? ''}`
  const [seenIdentity, setSeenIdentity] = useState(identity)
  const [draft, setDraft] = useState(authoritativeDraft)
  const [syncFailed, setSyncFailed] = useState(false)
  const [localError, setLocalError] = useState<string | null>(null)
  const { saving, error, save, clear } = useSetCodexAccountUsageStop(runId, identity)
  const beginFlow = useOwnedFlow(runId, identity)

  if (seenIdentity !== identity) {
    setSeenIdentity(identity)
    setDraft(authoritativeDraft)
    setSyncFailed(false)
    setLocalError(null)
  }

  const commit = async (action: () => Promise<boolean>, onAccepted?: () => void) => {
    const owns = beginFlow()
    setSyncFailed(false)
    setLocalError(null)
    if (!(await action()) || !owns()) {
      return false
    }
    onAccepted?.()
    if (onSaved && !(await onSaved()) && owns()) {
      setSyncFailed(true)
    }
    return true
  }

  const handleSave = async () => {
    if (draft === '') {
      setLocalError(EMPTY_SAVE_MESSAGE)
      return
    }
    await commit(() => save(draft))
  }

  const handleClear = async () => {
    await commit(clear, () => setDraft(''))
  }

  const shownError = localError ?? error

  return (
    <div className="dc-codex-account-usage-stop" role="group" aria-label="Codex account-usage stop">
      <span className="dc-codex-account-usage-stop-title">Codex account-usage stop</span>
      <span className="dc-codex-account-usage-stop-current">Current run setting: {describeRunAccountUsageStop(setting)}</span>
      <span className="dc-codex-account-usage-stop-note">{NOTE}</span>
      {editable ? (
        <>
          <input
            type="text"
            inputMode="numeric"
            aria-label="Codex account-usage stop percentage"
            value={draft}
            onChange={(event) => {
              setDraft(event.target.value)
              setLocalError(null)
            }}
            disabled={saving}
            aria-invalid={shownError ? true : undefined}
          />
          <button type="button" aria-label="Save Codex account-usage stop" onClick={() => void handleSave()} disabled={saving}>
            Save
          </button>
          <button type="button" aria-label="Clear Codex account-usage stop" onClick={() => void handleClear()} disabled={saving}>
            Clear
          </button>
        </>
      ) : (
        <span className="dc-codex-account-usage-stop-readonly">This run&apos;s Codex account-usage stop can no longer be changed.</span>
      )}
      {shownError && (
        <span className="dc-codex-account-usage-stop-error" role="alert">
          {shownError}
        </span>
      )}
      {syncFailed && <span className="dc-codex-account-usage-stop-error">{SYNC_FAILURE_MESSAGE}</span>}
    </div>
  )
}
