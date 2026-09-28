import { useEffect, useState } from 'react'
import { useCodexModelCatalog } from '../hooks/useCodexModelCatalog'
import { useSetCodexAssignmentPreference } from '../hooks/useSetCodexAssignmentPreference'

interface CodexAssignmentPreferenceControlProps {
  runId: string
  requestedCodexModel: string | null
  requestedCodexEffort: string | null
}

/**
 * Lets the owner explicitly request a Codex model and optional reasoning effort for this run's
 * later Planner, Challenge Resolver, and Code Reviewer claims, using the existing read-only Codex
 * model catalog to populate the choices. Never auto-selects the catalog's own suggested default —
 * the model select always starts on an explicit "No preference" option. A change here never
 * affects an already-claimed attempt's own immutable assignment; it only takes effect at a later
 * claim. This control shows only the owner's current *requested* preference, never an effective,
 * observed, or invocation-eligible value.
 */
export function CodexAssignmentPreferenceControl({
  runId,
  requestedCodexModel,
  requestedCodexEffort,
}: CodexAssignmentPreferenceControlProps) {
  const { catalog, loading: catalogLoading, error: catalogError } = useCodexModelCatalog()
  const [saved, setSaved] = useState({ model: requestedCodexModel, effort: requestedCodexEffort })
  const [selectedModel, setSelectedModel] = useState(requestedCodexModel ?? '')
  const [selectedEffort, setSelectedEffort] = useState(requestedCodexEffort ?? '')

  // The parent cockpit projection is the durable source of truth (e.g. after a page reload);
  // a locally saved value from this component's own successful save is otherwise preferred so
  // the control never appears to "revert" while waiting for the next cockpit read.
  useEffect(() => {
    setSaved({ model: requestedCodexModel, effort: requestedCodexEffort })
    setSelectedModel(requestedCodexModel ?? '')
    setSelectedEffort(requestedCodexEffort ?? '')
    // eslint-disable-next-line react-hooks/exhaustive-deps -- intentionally resyncs only when the run identity or its durable value changes
  }, [runId, requestedCodexModel, requestedCodexEffort])

  const { saving, error: saveError, save } = useSetCodexAssignmentPreference(() => {})

  const models = catalog?.status === 'Observed' ? (catalog.models ?? []) : []
  const selectedModelEntry = models.find((model) => model.id === selectedModel)
  const availableEfforts = selectedModelEntry?.supportedReasoningEfforts ?? []

  const handleModelChange = (value: string) => {
    setSelectedModel(value)
    setSelectedEffort('')
  }

  const handleSave = async () => {
    const model = selectedModel.length > 0 ? selectedModel : null
    const effort = model !== null && selectedEffort.length > 0 ? selectedEffort : null
    const succeeded = await save(runId, model, effort)
    if (succeeded) {
      setSaved({ model, effort })
    }
  }

  const handleClear = async () => {
    const succeeded = await save(runId, null, null)
    if (succeeded) {
      setSaved({ model: null, effort: null })
      setSelectedModel('')
      setSelectedEffort('')
    }
  }

  const currentLabel =
    saved.model === null
      ? 'No requested Codex model/effort for future claims.'
      : `Requested for future claims: ${saved.model}${saved.effort ? ` (${saved.effort})` : ''}.`

  return (
    <div className="dc-codex-assignment-preference" aria-label="Codex model and effort preference">
      <span className="dc-codex-assignment-preference-current">{currentLabel}</span>
      {catalogLoading && <span className="dc-codex-assignment-preference-status">Loading Codex model catalog…</span>}
      {!catalogLoading && catalogError && (
        <span className="dc-codex-assignment-preference-status">Codex model catalog: Unknown</span>
      )}
      {!catalogLoading && !catalogError && (
        <>
          <select
            aria-label="Requested Codex model"
            value={selectedModel}
            onChange={(event) => handleModelChange(event.target.value)}
            disabled={saving}
          >
            <option value="">No preference</option>
            {models.map((model) => (
              <option key={model.id} value={model.id}>
                {model.displayName ?? model.id}
              </option>
            ))}
          </select>
          <select
            aria-label="Requested Codex reasoning effort"
            value={selectedEffort}
            onChange={(event) => setSelectedEffort(event.target.value)}
            disabled={saving || selectedModel.length === 0 || availableEfforts.length === 0}
          >
            <option value="">No effort preference</option>
            {availableEfforts.map((effort) => (
              <option key={effort} value={effort}>
                {effort}
              </option>
            ))}
          </select>
        </>
      )}
      <button type="button" onClick={() => void handleSave()} disabled={saving || (catalogLoading && selectedModel.length > 0)}>
        Save
      </button>
      <button type="button" onClick={() => void handleClear()} disabled={saving || saved.model === null}>
        Clear
      </button>
      {saveError && <span className="dc-codex-assignment-preference-error">{saveError}</span>}
    </div>
  )
}
