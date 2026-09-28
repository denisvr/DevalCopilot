/** One picker-visible Codex model. Structurally matches the generated
 * `CodexModelCatalogEntryResponse`. */
export interface CodexModelCatalogEntryView {
  id?: string
  displayName?: string
  supportedReasoningEfforts?: string[] | null
  defaultReasoningEffort?: string
}

/** The host-scoped Codex model catalog observation. Structurally matches the generated
 * `CodexModelCatalogResponse`. `status` is `'Observed'` or `'Unknown'` — every unavailable case
 * (no vetted launch target, an unsupported protocol method, a malformed response, a timeout, or a
 * process failure) is folded into the same `'Unknown'` value, never displayed as an empty-looking
 * catalog. */
export interface CodexModelCatalogView {
  status?: string
  retrievedAtUtc?: string | Date
  models?: CodexModelCatalogEntryView[]
}

/**
 * `supportedReasoningEfforts` distinguishes three states: absent/`null` (the reported list could
 * not be trusted as a whole — Unknown), a genuinely empty array (the provider reported none —
 * None, not Unknown), and a real list (joined as given). A partial list with a bad element
 * silently dropped is never displayed — the backend already collapses that case to Unknown.
 */
function describeSupportedReasoningEfforts(efforts: string[] | null | undefined): string {
  if (efforts === undefined || efforts === null) {
    return 'Unknown'
  }

  return efforts.length > 0 ? efforts.join(', ') : 'None'
}

function describeModel(model: CodexModelCatalogEntryView): string {
  const displayName = model.displayName ?? model.id ?? 'Unknown model'
  const effortsLabel = describeSupportedReasoningEfforts(model.supportedReasoningEfforts)
  const defaultLabel = model.defaultReasoningEffort ?? 'Unknown'
  return `${displayName} — efforts: ${effortsLabel}; default: ${defaultLabel}`
}

/**
 * One line per catalog model — models are never combined into one invented aggregate. Never
 * displays an unpopulated catalog as if it were real data: an `Unknown` status, a status this
 * reader does not recognize, or no models at all always renders as a single explicit "Unknown"
 * line.
 */
export function describeCodexModelCatalog(catalog: CodexModelCatalogView | null | undefined): string[] {
  if (!catalog || catalog.status !== 'Observed' || !catalog.models || catalog.models.length === 0) {
    return ['Codex model catalog: Unknown']
  }

  return catalog.models.map(describeModel)
}

export function describeCodexModelCatalogRetrievedAt(catalog: CodexModelCatalogView | null | undefined): string | null {
  if (!catalog || catalog.status !== 'Observed' || !catalog.retrievedAtUtc) {
    return null
  }

  const retrievedAt = new Date(catalog.retrievedAtUtc)
  return Number.isNaN(retrievedAt.getTime()) ? null : `Retrieved ${retrievedAt.toLocaleString()}`
}
