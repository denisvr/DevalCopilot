import { useState } from 'react'
import type { StageMapEntryResponse } from '../../../api/clients'

interface WorkflowRailProps {
  stageMap: StageMapEntryResponse[]
}

/**
 * A projection of persisted stages. Collapsing releases its width to Agent
 * Collaboration; a compact progress indicator remains visible while collapsed.
 */
export function WorkflowRail({ stageMap }: WorkflowRailProps) {
  const [collapsed, setCollapsed] = useState(false)
  const completedCount = stageMap.filter((stage) => stage.isCompleted).length

  return (
    <aside className="dc-rail" data-collapsed={collapsed} aria-label="Workflow">
      <div className="dc-rail-header">
        {!collapsed && <span className="dc-rail-title">Workflow</span>}
        <button
          type="button"
          className="dc-nav-toggle"
          onClick={() => setCollapsed((value) => !value)}
          aria-label={collapsed ? 'Expand workflow rail' : 'Collapse workflow rail'}
        >
          {collapsed ? '›' : '‹'}
        </button>
      </div>
      {collapsed ? (
        <div title={`${completedCount} of ${stageMap.length} stages complete`}>
          {completedCount}/{stageMap.length}
        </div>
      ) : (
        <ol className="dc-stage-list">
          {stageMap.map((stage) => (
            <li key={stage.stage} className="dc-stage-item" data-completed={stage.isCompleted} data-active={stage.isActive}>
              <span className="dc-stage-dot" />
              {stage.stage}
            </li>
          ))}
        </ol>
      )}
    </aside>
  )
}
