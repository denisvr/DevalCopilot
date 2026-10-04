import type { AgentModelContextLimitsResponse } from '../../../api/clients'

interface ModelContextLimitsFactProps {
  /** The attempt's provider as the evidence response names it; the fact exists only for a Claude attempt. */
  provider?: string
  /** The limits recorded when the attempt concluded; null/undefined when not recorded or unknown. */
  limits?: AgentModelContextLimitsResponse | null
  className?: string
}

const NOT_RECORDED = 'Not recorded'

function formatTokens(value: number | undefined): string {
  return typeof value === 'number' && Number.isSafeInteger(value) && value > 0 ? value.toLocaleString('en-US') : NOT_RECORDED
}

/**
 * Presents the model identifiers Claude listed in its own result for one concluded attempt, each with the context-window and
 * maximum-output token limits Claude reported for it. It is historical, provider-reported observation: the entries are models
 * listed by Claude, never proof that a model was used, and the remaining context and the capacity of a next invocation were
 * not measured. Nothing here is derived from token usage (no percentage or meter), and an absent record is "Not recorded",
 * never zero. Identifiers are rendered as plain React text.
 */
export function ModelContextLimitsFact({ provider, limits, className }: ModelContextLimitsFactProps) {
  if (provider !== 'ClaudeCode') {
    return null
  }

  const models = limits?.models ?? []
  const cls = ['dc-model-context-limits', className].filter(Boolean).join(' ')

  return (
    <section className={cls} aria-label="Claude-reported model limits">
      <h4>Claude-reported model limits</h4>
      {models.length === 0 ? (
        <p>{NOT_RECORDED}</p>
      ) : (
        <>
          <p>Models listed by Claude in its result for this attempt (not independently proven to have been used):</p>
          <table>
            <thead>
              <tr>
                <th scope="col">Model identifier</th>
                <th scope="col">Context-window tokens</th>
                <th scope="col">Maximum output tokens</th>
              </tr>
            </thead>
            <tbody>
              {models.map((model, index) => (
                <tr key={`${index}:${model.modelId ?? ''}`}>
                  <th scope="row" className="dc-model-context-limits-id">
                    {model.modelId ? model.modelId : NOT_RECORDED}
                  </th>
                  <td>{formatTokens(model.contextWindowTokens)}</td>
                  <td>{formatTokens(model.maxOutputTokens)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}
      <p className="dc-card-evidence-caveat">
        Reported by Claude in its result for this attempt. Remaining context and the capacity of the next invocation were not measured.
      </p>
    </section>
  )
}
