import type { GetRunCockpitResponse } from '../../../api/clients'
import { formatDuration } from '../formatDuration'
import { useTickingDuration } from '../hooks/useTickingDuration'

interface RunHeaderProps {
  cockpit: GetRunCockpitResponse
}

const LIFECYCLE_TONE: Record<string, string> = {
  Running: 'running',
  Completed: 'completed',
}

/**
 * The primary run header: objective, execution number, lifecycle, and the autonomous
 * session timer. Pause and Stop are rendered — not hidden — but disabled: this
 * increment does not implement their behavior, and the spec forbids implying that a
 * control works when it does not.
 */
export function RunHeader({ cockpit }: RunHeaderProps) {
  const isRunning = cockpit.lifecycle === 'Running'
  const autonomousSeconds = useTickingDuration(cockpit.autonomousDurationSeconds ?? 0, isRunning)

  return (
    <header className="dc-run-header">
      <div>
        <h1>{cockpit.objective}</h1>
        <div className="dc-run-header-meta">
          {cockpit.projectName} · Execution {cockpit.executionNumber}
        </div>
      </div>
      <span className="dc-badge" data-tone={LIFECYCLE_TONE[cockpit.lifecycle ?? ''] ?? 'default'}>
        {cockpit.lifecycle} · {cockpit.stage}
      </span>
      <span className="dc-badge" title="Autonomous session time: excludes paused and terminal time">
        ⏱ {formatDuration(autonomousSeconds)}
      </span>
      <div className="dc-run-controls">
        <button type="button" className="dc-button" disabled title="Not available in this increment">
          Pause
        </button>
        <button type="button" className="dc-button" disabled title="Not available in this increment">
          Stop
        </button>
      </div>
    </header>
  )
}
