import { useState } from 'react'
import { useCodexAccountAllowance } from '../hooks/useCodexAccountAllowance'
import { describeCodexAccountAllowance, describeCodexAccountAllowanceRetrievedAt } from '../describeCodexAccountAllowance'
import { useCodexModelCatalog } from '../hooks/useCodexModelCatalog'
import { describeCodexModelCatalog, describeCodexModelCatalogRetrievedAt } from '../describeCodexModelCatalog'
import { AgentAttemptHistoryPanel } from './AgentAttemptHistoryPanel'

const EVIDENCE_TABS = ['Changes', 'Local verification', 'Review findings', 'GitHub CI', 'Artifacts', 'Approvals']

/**
 * Structurally present per the approved specification; this increment has not yet collected
 * most of this data. Every tab still shows a concise, neutral "not yet collected" state rather than an
 * invented number, percentage, or status — except the Codex account-allowance line (see
 * `useCodexAccountAllowance`) and the Codex model/reasoning-effort catalog line (see
 * `useCodexModelCatalog`), which now read real, read-only observations. Claude account usage
 * remains "not yet collected" here; that observation is a separate, not-yet-selected slice.
 */
interface UsageEvidenceRailProps {
  /** The run whose Agent attempt history the rail offers; the history is omitted when absent. */
  runId?: string
}

export function UsageEvidenceRail({ runId }: UsageEvidenceRailProps = {}) {
  const [collapsed, setCollapsed] = useState(false)
  const { allowance, loading, error, refresh } = useCodexAccountAllowance(!collapsed)
  const {
    catalog,
    loading: catalogLoading,
    error: catalogError,
    refresh: refreshCatalog,
  } = useCodexModelCatalog(!collapsed)

  const codexUsageLines = loading
    ? ['Codex account usage: loading…']
    : error
      ? ['Codex account usage: Unknown']
      : describeCodexAccountAllowance(allowance)
  const retrievedAtLine = loading || error ? null : describeCodexAccountAllowanceRetrievedAt(allowance)

  const codexModelCatalogLines = catalogLoading
    ? ['Codex model catalog: loading…']
    : catalogError
      ? ['Codex model catalog: Unknown']
      : describeCodexModelCatalog(catalog)
  const catalogRetrievedAtLine = catalogLoading || catalogError ? null : describeCodexModelCatalogRetrievedAt(catalog)

  return (
    <aside className="dc-rail" data-side="right" data-collapsed={collapsed} aria-label="Usage and evidence">
      <div className="dc-rail-header">
        {!collapsed && <span className="dc-rail-title">Usage &amp; Evidence</span>}
        <button
          type="button"
          className="dc-nav-toggle"
          onClick={() => setCollapsed((value) => !value)}
          aria-label={collapsed ? 'Expand usage and evidence rail' : 'Collapse usage and evidence rail'}
        >
          {collapsed ? '‹' : '›'}
        </button>
      </div>
      {!collapsed && (
        <>
          <div className="dc-codex-account-allowance" data-status={loading || error ? 'Unknown' : (allowance?.status ?? 'Unknown')}>
            {codexUsageLines.map((line, index) => (
              // eslint-disable-next-line react/no-array-index-key -- lines are a stable-order projection, not a reorderable list
              <span key={index}>{line}</span>
            ))}
            {retrievedAtLine && <span className="dc-codex-account-allowance-retrieved-at">{retrievedAtLine}</span>}
            <button
              type="button"
              className="dc-refresh-button"
              onClick={refresh}
              disabled={loading}
              aria-label="Refresh Codex account usage"
            >
              Refresh
            </button>
          </div>
          <div
            className="dc-codex-model-catalog"
            data-status={catalogLoading || catalogError ? 'Unknown' : (catalog?.status ?? 'Unknown')}
          >
            {codexModelCatalogLines.map((line, index) => (
              // eslint-disable-next-line react/no-array-index-key -- lines are a stable-order projection, not a reorderable list
              <span key={index}>{line}</span>
            ))}
            {catalogRetrievedAtLine && <span className="dc-codex-model-catalog-retrieved-at">{catalogRetrievedAtLine}</span>}
            <button
              type="button"
              className="dc-refresh-button"
              onClick={refreshCatalog}
              disabled={catalogLoading}
              aria-label="Refresh Codex model catalog"
            >
              Refresh
            </button>
          </div>
          <div className="dc-placeholder">Claude account usage: not yet collected in this increment.</div>
          {EVIDENCE_TABS.map((tab) => (
            <div key={tab} className="dc-placeholder">
              {tab}: not yet collected.
            </div>
          ))}
          {runId && <AgentAttemptHistoryPanel key={runId} runId={runId} />}
        </>
      )}
    </aside>
  )
}
