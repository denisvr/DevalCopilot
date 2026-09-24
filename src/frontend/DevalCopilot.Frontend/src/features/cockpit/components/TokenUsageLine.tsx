import { describeTokenUsage, hasKnownTokenUsage, type TokenUsageView } from '../describeTokenUsage'

interface TokenUsageLineProps {
  tokenUsage: TokenUsageView | null | undefined
  dispatchedAtUtc: Date | string | null | undefined
  status: string | null | undefined
}

/**
 * Renders an attempt's provider-reported token usage on its own line, next to (never merged
 * with) its process-evidence line and semantic outcome. Only bounded counts are rendered — never
 * a schema version, path, argument, environment value, output, session identifier, or credential.
 */
export function TokenUsageLine({ tokenUsage, dispatchedAtUtc, status }: TokenUsageLineProps) {
  const text = describeTokenUsage(tokenUsage, {
    dispatched: Boolean(dispatchedAtUtc),
    running: status === 'Running',
  })

  return (
    <p className="dc-token-usage" data-token-usage={hasKnownTokenUsage(tokenUsage) ? 'Known' : 'Unknown'}>
      {text}
    </p>
  )
}
