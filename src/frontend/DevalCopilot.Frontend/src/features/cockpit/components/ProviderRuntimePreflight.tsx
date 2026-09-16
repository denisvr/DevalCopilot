import type { ProviderRuntimePreflightResponse } from '../../../api/clients'

interface ProviderRuntimePreflightProps {
  providers: ProviderRuntimePreflightResponse[]
  loading: boolean
  error: string | null
  refreshingCapability: string | null
  onRefresh: (capability: string) => void
}

const STATUS_LABEL: Record<string, string> = {
  Checking: 'Checking…',
  Available: 'Version observed',
  Unavailable: 'Unavailable',
  NeedsAttention: 'Needs attention',
  Degraded: 'Degraded',
}

const PROVIDER_CAPABILITY: Record<string, string> = {
  Codex: 'CodexCli',
  ClaudeCode: 'ClaudeCli',
}

const PROVIDER_LABEL: Record<string, string> = {
  Codex: 'Codex',
  ClaudeCode: 'Claude Code',
}

/**
 * Shows only host version-probe evidence. Provider controls are deliberately explicit about
 * what this preflight cannot observe, so an installed runtime is never presented as invocable.
 */
export function ProviderRuntimePreflight({
  providers,
  loading,
  error,
  refreshingCapability,
  onRefresh,
}: ProviderRuntimePreflightProps) {
  if (loading) {
    return <p className="dc-provider-runtime-preflight-state">Checking provider runtimes…</p>
  }

  if (error) {
    return <p className="dc-provider-runtime-preflight-state">{error}</p>
  }

  return (
    <section className="dc-provider-runtime-preflight" aria-label="Provider runtime preflight">
      <h2>Provider runtimes</h2>
      <p>Version observation does not prove authentication or invocation readiness.</p>
      <ul>
        {providers.map((provider) => {
          const providerName = provider.provider ?? ''
          const capability = PROVIDER_CAPABILITY[providerName]
          const isRefreshing = refreshingCapability === capability
          const status = provider.status ?? 'Checking'
          const isStale = provider.evidenceFreshness === 'Stale'

          return (
            <li key={providerName} className="dc-provider-runtime-row" data-status={status} data-stale={isStale}>
              <div>
                <strong>{PROVIDER_LABEL[providerName] ?? providerName}</strong>
                <span>{STATUS_LABEL[status] ?? status}{isStale ? ' (stale)' : ''}</span>
                {provider.observedVersion ? <code>{provider.observedVersion}</code> : null}
                <span>{provider.reasonMessage}</span>
              </div>
              <span className="dc-provider-runtime-unknown">Authentication and runtime controls: Unknown.</span>
              <button
                type="button"
                className="dc-capability-refresh"
                disabled={isRefreshing || !capability}
                onClick={() => {
                  if (capability) {
                    onRefresh(capability)
                  }
                }}
              >
                {isRefreshing ? 'Refreshing…' : 'Refresh'}
              </button>
            </li>
          )
        })}
      </ul>
    </section>
  )
}
