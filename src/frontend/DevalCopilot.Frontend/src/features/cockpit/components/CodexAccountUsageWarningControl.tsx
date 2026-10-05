import { useState } from 'react'
import type { CodexAccountUsageWarningSettingResponse } from '../../../api/clients'
import { configuredWarningPercent, describeRunAccountUsageWarning } from '../describeCodexAccountUsageWarning'
import { useCheckCodexAccountUsageWarning } from '../hooks/useCheckCodexAccountUsageWarning'
import { useOwnedFlow } from '../hooks/useRunActionLifetime'
import { useSetCodexAccountUsageWarning } from '../hooks/useSetCodexAccountUsageWarning'

const SYNC_FAILURE_MESSAGE = 'Saved, but the cockpit could not be refreshed; the displayed setting may be out of date.'
const EMPTY_SAVE_MESSAGE = 'Enter a whole number from 1 to 100, or use Clear.'
const NOTE =
  'Advisory only. When you explicitly check the Codex account, this shows whether a reported account-usage window has reached this percentage. It never blocks, reserves or stops any request, and it is separate from the account-usage stop. A check describes the host’s Codex account, not usage attributable to this run, and shows no remaining capacity.'
const NOT_CHECKED = 'Not checked in this view.'
const PENDING = 'Checking the Codex account…'
const FAILED = 'The Codex account could not be checked. Try again.'

interface CodexAccountUsageWarningControlProps {
  runId: string
  /** The run's current saved warning from the authoritative cockpit projection. */
  setting: CodexAccountUsageWarningSettingResponse | null | undefined
  /** False once the run lifecycle no longer allows changes; the setting is then shown read-only. */
  editable: boolean
  /** Re-queries the authoritative cockpit; resolves false on failure or if the run changed meanwhile. */
  onSaved?: () => Promise<boolean>
}

/**
 * Lets the owner save an optional advisory used-percent warning (1..100) for this run and explicitly
 * check the host's Codex account against it. Nothing here reads the account except the explicit
 * check button: not mounting, saving, clearing, selecting a run or a cockpit refresh. The value
 * shown is always the cockpit's saved setting; drafts, pending state, messages and the last
 * observation are owned by the run plus that authoritative setting, so a stale completion never
 * writes or refreshes a replacement selection.
 */
export function CodexAccountUsageWarningControl({ runId, setting, editable, onSaved }: CodexAccountUsageWarningControlProps) {
  const percent = configuredWarningPercent(setting)
  const authoritativeDraft = percent === null ? '' : String(percent)
  const identity = `${runId}|${setting?.state ?? ''}|${setting?.percent ?? ''}`
  const [seenIdentity, setSeenIdentity] = useState(identity)
  const [draft, setDraft] = useState(authoritativeDraft)
  const [syncFailed, setSyncFailed] = useState(false)
  const [localError, setLocalError] = useState<string | null>(null)
  const { saving, error, save, clear } = useSetCodexAccountUsageWarning(runId, identity)
  const { phase, check } = useCheckCodexAccountUsageWarning(runId, identity, percent)
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
    <div className="dc-codex-account-usage-warning" role="group" aria-label="Codex account-usage warning">
      <span className="dc-codex-account-usage-warning-title">Codex account-usage warning</span>
      <span className="dc-codex-account-usage-warning-current">Current run setting: {describeRunAccountUsageWarning(setting)}</span>
      <span className="dc-codex-account-usage-warning-note">{NOTE}</span>
      {editable ? (
        <>
          <input
            type="text"
            inputMode="numeric"
            aria-label="Codex account-usage warning percentage"
            value={draft}
            onChange={(event) => {
              setDraft(event.target.value)
              setLocalError(null)
            }}
            disabled={saving}
            aria-invalid={shownError ? true : undefined}
          />
          <button type="button" aria-label="Save Codex account-usage warning" onClick={() => void handleSave()} disabled={saving}>
            Save
          </button>
          <button type="button" aria-label="Clear Codex account-usage warning" onClick={() => void handleClear()} disabled={saving}>
            Clear
          </button>
        </>
      ) : (
        <span className="dc-codex-account-usage-warning-readonly">This run&apos;s Codex account-usage warning can no longer be changed.</span>
      )}
      {shownError && (
        <span className="dc-codex-account-usage-warning-error" role="alert">
          {shownError}
        </span>
      )}
      {syncFailed && <span className="dc-codex-account-usage-warning-error">{SYNC_FAILURE_MESSAGE}</span>}
      {percent !== null && (
        <div className="dc-codex-account-usage-warning-check">
          <button
            type="button"
            aria-label="Check Codex account warning"
            onClick={() => void check()}
            disabled={phase.phase === 'pending'}
          >
            Check Codex account warning
          </button>
          <div
            className="dc-codex-account-usage-warning-result"
            role="status"
            data-result={phase.phase === 'observed' ? phase.view.kind : phase.phase}
          >
            {phase.phase === 'idle' && <span>{NOT_CHECKED}</span>}
            {phase.phase === 'pending' && <span>{PENDING}</span>}
            {phase.phase === 'failed' && <span>{FAILED}</span>}
            {phase.phase === 'observed' && (
              <>
                <span>{phase.view.headline}</span>
                {phase.view.kind !== 'unavailable' && (
                  <>
                    <span>Host retrieval time: {phase.view.observedAt}</span>
                    {phase.view.details.map((line) => (
                      <span key={line}>{line}</span>
                    ))}
                  </>
                )}
              </>
            )}
          </div>
        </div>
      )}
    </div>
  )
}
