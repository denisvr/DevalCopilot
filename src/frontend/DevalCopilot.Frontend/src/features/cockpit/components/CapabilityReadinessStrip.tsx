import type { CapabilityReadinessResponse } from '../../../api/clients'

interface CapabilityReadinessStripProps {
  capabilities: CapabilityReadinessResponse[]
  refreshingCapability: string | null
  onRefresh: (capability: string) => void
}

const STATUS_LABEL: Record<string, string> = {
  Ready: 'Ready',
  Degraded: 'Degraded',
  NeedsAttention: 'Needs attention',
  Unavailable: 'Unavailable',
}

/**
 * Projects the one shared host observation for every registered project — the same
 * `capabilities` array (same `lastCheckedUtc`/`version` values) is expected regardless of which
 * project is selected. `displayStatus` is null only while a capability has never been probed
 * yet, presented here as "Checking…" rather than a fifth persisted status.
 */
export function CapabilityReadinessStrip({ capabilities, refreshingCapability, onRefresh }: CapabilityReadinessStripProps) {
  if (capabilities.length === 0) {
    return null
  }

  return (
    <ul className="dc-capability-strip" aria-label="Environment readiness">
      {capabilities.map((capability) => {
        const capabilityName = capability.capability ?? ''
        const status = capability.displayStatus ?? undefined
        const label = status ? (STATUS_LABEL[status] ?? status) : 'Checking…'
        const isRefreshing = refreshingCapability === capabilityName

        return (
          <li
            key={capabilityName}
            className="dc-capability-chip"
            data-status={status ?? 'checking'}
            data-required={capability.isRequired}
            data-stale={capability.isStale}
          >
            <span className="dc-capability-name">{capabilityName}</span>
            <span className="dc-capability-status">
              {label}
              {capability.isStale ? ' (stale)' : ''}
            </span>
            {capability.version ? <span className="dc-capability-version">{capability.version}</span> : null}
            <button
              type="button"
              className="dc-capability-refresh"
              disabled={isRefreshing}
              onClick={() => onRefresh(capabilityName)}
            >
              {isRefreshing ? 'Refreshing…' : 'Refresh'}
            </button>
          </li>
        )
      })}
    </ul>
  )
}
