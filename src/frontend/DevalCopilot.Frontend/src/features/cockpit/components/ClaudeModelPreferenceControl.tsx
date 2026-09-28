import { useState } from 'react'
import { useSetClaudeModelPreference } from '../hooks/useSetClaudeModelPreference'

/**
 * The closed set of Claude CLI `--model` aliases the backend accepts. The server validates every
 * request; this list only offers the choices. Membership is request syntax, not proof that an
 * alias is enabled for the signed-in account.
 */
const CLAUDE_MODEL_ALIASES = ['sonnet', 'opus', 'haiku'] as const

interface ClaudeModelPreferenceControlProps {
  runId: string
  requestedClaudeModel: string | null
}

/**
 * Lets the owner explicitly request a Claude model alias for this run's later Critical reviewer,
 * Implementer, and review-correction attempts. The model select always starts on an explicit
 * "No preference" option and never auto-selects an alias. A change here never affects an
 * already-claimed attempt's own immutable request; it only takes effect at a later claim. This
 * control shows only the owner's *requested* alias, never an observed, effective, or
 * account-available model — the provider may reject an alias and no fallback model is used.
 * The parent keys this component by run and durable value, so it remounts from the cockpit
 * projection (the source of truth) whenever either changes.
 */
export function ClaudeModelPreferenceControl({ runId, requestedClaudeModel }: ClaudeModelPreferenceControlProps) {
  const [saved, setSaved] = useState<string | null>(requestedClaudeModel)
  const [selected, setSelected] = useState(requestedClaudeModel ?? '')
  const { saving, error, save } = useSetClaudeModelPreference()

  const handleSave = async () => {
    const model = selected.length > 0 ? selected : null
    if (await save(runId, model)) {
      setSaved(model)
    }
  }

  const handleClear = async () => {
    if (await save(runId, null)) {
      setSaved(null)
      setSelected('')
    }
  }

  const currentLabel =
    saved === null
      ? 'No Claude model requested for future attempts: no model argument will be passed. The model actually used is not observed.'
      : `Requested Claude model for future attempts: ${saved}. This is a request only; the model actually used is not observed.`

  return (
    <div className="dc-claude-model-preference" aria-label="Claude model request">
      <span className="dc-claude-model-preference-current">{currentLabel}</span>
      <select
        aria-label="Requested Claude model"
        value={selected}
        onChange={(event) => setSelected(event.target.value)}
        disabled={saving}
      >
        <option value="">No preference</option>
        {CLAUDE_MODEL_ALIASES.map((alias) => (
          <option key={alias} value={alias}>
            {alias}
          </option>
        ))}
      </select>
      <button type="button" onClick={() => void handleSave()} disabled={saving}>
        Save
      </button>
      <button type="button" onClick={() => void handleClear()} disabled={saving || saved === null}>
        Clear
      </button>
      {error && <span className="dc-claude-model-preference-error">{error}</span>}
    </div>
  )
}
