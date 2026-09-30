import { useState } from 'react'
import type { ClaudeMutationTurnLimitResponse } from '../../../api/clients'
import { describeClaudeRunTurnLimit } from '../describeClaudeTurnLimit'
import { useSetClaudeMutationTurnLimit } from '../hooks/useSetClaudeMutationTurnLimit'

const SYNC_FAILURE_MESSAGE = 'Saved, but the cockpit could not be refreshed; the displayed request may be out of date.'
const EMPTY_SAVE_MESSAGE = 'Enter a whole number from 1 to 100, or use Clear.'

interface ClaudeMutationTurnLimitControlProps {
  runId: string
  /** The run's current saved request from the authoritative cockpit projection. */
  request: ClaudeMutationTurnLimitResponse | null | undefined
  /** False once the run lifecycle no longer allows changes; the request is then shown read-only. */
  editable: boolean
  /** Re-queries the authoritative cockpit; resolves false on failure or if the run changed meanwhile. */
  onSaved?: () => Promise<boolean>
}

/**
 * Lets the owner request a whole-number agentic-turn guardrail (1..100) for this run's FUTURE
 * Claude implementation and review-correction attempts. It is a request only: not an account,
 * token, or cost limit, not enforced by this host, and never a measured turn count. It never
 * affects an attempt already claimed. The value shown is always the cockpit's saved request;
 * a successful save or clear re-queries the cockpit, and the parent keys this component by run and
 * saved value so the draft re-initialises from that authoritative state.
 */
export function ClaudeMutationTurnLimitControl({ runId, request, editable, onSaved }: ClaudeMutationTurnLimitControlProps) {
  const [draft, setDraft] = useState(request?.state === 'Requested' && request.maxTurns !== undefined ? String(request.maxTurns) : '')
  const [syncFailed, setSyncFailed] = useState(false)
  const [localError, setLocalError] = useState<string | null>(null)
  const { saving, error, save, clear } = useSetClaudeMutationTurnLimit(runId)

  const commit = async (action: () => Promise<boolean>) => {
    setSyncFailed(false)
    setLocalError(null)
    if (!(await action())) {
      return false
    }
    if (onSaved && !(await onSaved())) {
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
    if (await commit(clear)) {
      setDraft('')
    }
  }

  const current = describeClaudeRunTurnLimit(request)
  const shownError = localError ?? error

  return (
    <div className="dc-claude-turn-limit" role="group" aria-label="Claude turn limit">
      <span className="dc-claude-turn-limit-title">Claude turn limit</span>
      <span className="dc-claude-turn-limit-current">Current run request: {current}</span>
      <span className="dc-claude-turn-limit-note">
        A request applied to future Claude implementation and review-correction attempts only. It is not an account or
        host-enforced resource limit, and the turns actually used are not measured.
      </span>
      {editable ? (
        <>
          <input
            type="text"
            inputMode="numeric"
            aria-label="Requested Claude turn limit"
            value={draft}
            onChange={(event) => {
              setDraft(event.target.value)
              setLocalError(null)
            }}
            disabled={saving}
            aria-invalid={shownError ? true : undefined}
          />
          <button type="button" onClick={() => void handleSave()} disabled={saving}>
            Save
          </button>
          <button type="button" onClick={() => void handleClear()} disabled={saving}>
            Clear
          </button>
        </>
      ) : (
        <span className="dc-claude-turn-limit-readonly">This run&apos;s Claude turn limit request can no longer be changed.</span>
      )}
      {shownError && (
        <span className="dc-claude-turn-limit-error" role="alert">
          {shownError}
        </span>
      )}
      {syncFailed && <span className="dc-claude-turn-limit-error">{SYNC_FAILURE_MESSAGE}</span>}
    </div>
  )
}
