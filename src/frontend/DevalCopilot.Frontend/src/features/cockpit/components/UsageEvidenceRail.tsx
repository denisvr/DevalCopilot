import { useState } from 'react'

const EVIDENCE_TABS = ['Changes', 'Local verification', 'Review findings', 'GitHub CI', 'Artifacts', 'Approvals']

/**
 * Structurally present per the approved specification, but this increment collects
 * none of this data yet. Every tab shows a concise, neutral "not yet collected" state
 * rather than an invented number, percentage, or status.
 */
export function UsageEvidenceRail() {
  const [collapsed, setCollapsed] = useState(false)

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
          <div className="dc-placeholder">Provider-account usage: not yet collected in this increment.</div>
          {EVIDENCE_TABS.map((tab) => (
            <div key={tab} className="dc-placeholder">
              {tab}: not yet collected.
            </div>
          ))}
        </>
      )}
    </aside>
  )
}
