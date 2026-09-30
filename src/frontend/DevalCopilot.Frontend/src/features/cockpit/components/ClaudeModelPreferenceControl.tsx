import { useState } from 'react'
import { useOwnedFlow } from '../hooks/useRunActionLifetime'
import { useSetClaudeModelPreference } from '../hooks/useSetClaudeModelPreference'

/**
 * The closed set of Claude CLI `--model` aliases the backend accepts. The server validates every
 * request; this list only offers the choices. Membership is request syntax, not proof that an
 * alias is enabled for the signed-in account.
 */
const CLAUDE_MODEL_ALIASES = ['sonnet', 'opus', 'haiku'] as const

/** The closed, case-sensitive `--effort` levels the backend accepts (validated again server-side). */
const CLAUDE_EFFORT_LEVELS = ['low', 'medium', 'high'] as const

/** Only an explicitly requested sonnet or opus alias can carry an effort request; the server enforces it. */
function supportsEffort(model: string | null): boolean {
  return model === 'sonnet' || model === 'opus'
}

const SYNC_FAILURE_MESSAGE = 'Saved, but the cockpit could not be refreshed; the displayed request may be out of date.'

interface ClaudeModelPreferenceControlProps {
  runId: string
  requestedClaudeModel: string | null
  requestedClaudeEffort?: string | null
  /** Re-queries the authoritative cockpit; resolves false on failure or if the run changed meanwhile. */
  onSaved?: () => Promise<boolean>
}

/**
 * Lets the owner explicitly request a Claude model alias, and for sonnet or opus an optional
 * effort level, for this run's later Critical reviewer, Implementer, and review-correction
 * attempts. Both selects always start on an explicit "no request" option and never auto-select a
 * value. A change here never affects an already-claimed attempt's own immutable request; it only
 * takes effect at a later claim. This control shows only the owner's *requested* pair, never an
 * observed, effective, or account-available model or effort — the provider may reject or adjust a
 * request and no fallback is used. A successful save or clear re-queries the authoritative
 * cockpit, because the endpoint emits no run notification. The parent keys this component by run
 * and durable pair, so it remounts from the cockpit projection (the source of truth) whenever
 * either changes.
 */
export function ClaudeModelPreferenceControl({
  runId,
  requestedClaudeModel,
  requestedClaudeEffort = null,
  onSaved,
}: ClaudeModelPreferenceControlProps) {
  const identity = `${runId}|${requestedClaudeModel ?? ''}|${requestedClaudeEffort ?? ''}`
  const [seenIdentity, setSeenIdentity] = useState(identity)
  const [saved, setSaved] = useState<{ model: string | null; effort: string | null }>({
    model: requestedClaudeModel,
    effort: requestedClaudeEffort,
  })
  const [selectedModel, setSelectedModel] = useState(requestedClaudeModel ?? '')
  const [selectedEffort, setSelectedEffort] = useState(requestedClaudeEffort ?? '')
  const [syncFailed, setSyncFailed] = useState(false)
  const { saving, error, save } = useSetClaudeModelPreference(runId)
  const beginFlow = useOwnedFlow(runId, identity)

  // A different run or authoritative pair re-derives the selection, saved label, and warning
  // during render, so nothing owned by the previous identity is shown or kept.
  if (seenIdentity !== identity) {
    setSeenIdentity(identity)
    setSaved({ model: requestedClaudeModel, effort: requestedClaudeEffort })
    setSelectedModel(requestedClaudeModel ?? '')
    setSelectedEffort(requestedClaudeEffort ?? '')
    setSyncFailed(false)
  }

  const effortEnabled = supportsEffort(selectedModel.length > 0 ? selectedModel : null)

  // One flow = save, then local updates, then the authoritative refresh. Every continuation re-checks
  // that this flow still owns the control, so an older flow never clears a newer selection or writes a
  // stale warning. `onAccepted` runs as soon as the server accepted the change.
  const commit = async (model: string | null, effort: string | null, onAccepted?: () => void) => {
    const owns = beginFlow()
    setSyncFailed(false)
    if (!(await save(runId, model, effort)) || !owns()) {
      return false
    }
    setSaved({ model, effort })
    onAccepted?.()
    if (onSaved && !(await onSaved()) && owns()) {
      setSyncFailed(true)
    }
    return true
  }

  const handleModelChange = (value: string) => {
    setSelectedModel(value)
    if (!supportsEffort(value.length > 0 ? value : null)) {
      setSelectedEffort('')
    }
  }

  const handleSave = async () => {
    const model = selectedModel.length > 0 ? selectedModel : null
    const effort = supportsEffort(model) && selectedEffort.length > 0 ? selectedEffort : null
    await commit(model, effort)
  }

  const handleClear = async () => {
    await commit(null, null, () => {
      setSelectedModel('')
      setSelectedEffort('')
    })
  }

  const modelLabel =
    saved.model === null
      ? 'No Claude model requested for future attempts: no model argument will be passed. The model actually used is not observed.'
      : `Requested Claude model for future attempts: ${saved.model}. This is a request only; the model actually used is not observed.`

  const effortLabel =
    saved.effort === null
      ? 'No Claude effort requested for future attempts: no effort argument will be passed. The effort actually applied is not observed.'
      : `Requested Claude effort for future attempts: ${saved.effort}. This is a request only; the effort actually applied is not observed, and the provider may reject or adjust it.`

  return (
    <div className="dc-claude-model-preference" aria-label="Claude model request">
      <span className="dc-claude-model-preference-current">{modelLabel}</span>
      <span className="dc-claude-model-preference-current">{effortLabel}</span>
      <select
        aria-label="Requested Claude model"
        value={selectedModel}
        onChange={(event) => handleModelChange(event.target.value)}
        disabled={saving}
      >
        <option value="">No preference</option>
        {CLAUDE_MODEL_ALIASES.map((alias) => (
          <option key={alias} value={alias}>
            {alias}
          </option>
        ))}
      </select>
      <select
        aria-label="Requested Claude effort"
        value={selectedEffort}
        onChange={(event) => setSelectedEffort(event.target.value)}
        disabled={saving || !effortEnabled}
        title={effortEnabled ? undefined : 'An effort can be requested only together with sonnet or opus.'}
      >
        <option value="">No effort request</option>
        {CLAUDE_EFFORT_LEVELS.map((level) => (
          <option key={level} value={level}>
            {level}
          </option>
        ))}
      </select>
      <button type="button" onClick={() => void handleSave()} disabled={saving}>
        Save
      </button>
      <button
        type="button"
        onClick={() => void handleClear()}
        disabled={saving || (saved.model === null && saved.effort === null)}
      >
        Clear
      </button>
      {error && <span className="dc-claude-model-preference-error">{error}</span>}
      {syncFailed && <span className="dc-claude-model-preference-error">{SYNC_FAILURE_MESSAGE}</span>}
    </div>
  )
}
