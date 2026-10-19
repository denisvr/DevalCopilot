import type { GetRunCockpitResponse } from '../../../api/clients'
import { formatDuration } from '../formatDuration'
import { useTickingDuration } from '../hooks/useTickingDuration'
import type { PlanningSequenceControl } from '../planningSequence'

interface RunHeaderProps {
  cockpit: GetRunCockpitResponse
  /**
   * What the run's planning-sequence status established (ADR-0055). Anything but `none` (a sequence is recorded, or the status is not
   * established yet) shows the lifecycle exactly as recorded: the legacy `Running` lifecycle is never a live-execution indicator for a
   * coordinated run, and the retained duration is lifecycle elapsed time, not measured agent activity. Omitted: the run is not
   * coordinated.
   */
  planningSequenceControl?: PlanningSequenceControl
  /**
   * Whether the run executes real Agent attempts (ManualAgent, Legacy or an unrecognized mode; ADR-0056). For such a run the lifecycle is
   * shown exactly as recorded whatever the planning-sequence status, and the retained duration is lifecycle elapsed time that never
   * ticks as if it measured Agent activity. Only a simulated demo run keeps the live treatment. Omitted: the live treatment.
   */
  agentRun?: boolean
  /**
   * The host observation of the current Agent attempt, worded separately from the lifecycle (for example `Host observation: reporting`).
   * It is the observer's last record, never provider progress; null or omitted shows none.
   */
  hostObservation?: string | null
}

const LIFECYCLE_TONE: Record<string, string> = {
  Running: 'running',
  Completed: 'completed',
  Abandoned: 'abandoned',
}

const AUTONOMOUS_TIME_TITLE = 'Autonomous session time: excludes paused and terminal time'
const LIFECYCLE_ELAPSED_TITLE = 'Lifecycle elapsed time as recorded: not measured agent activity'
const RECORDED_LIFECYCLE_TITLE =
  'Recorded lifecycle. It is not a live-execution indicator: the host observation and any planning sequence are shown in their own regions.'
const HOST_OBSERVATION_TITLE =
  'What the host’s own observer last recorded for the current Agent attempt. It is not provider progress, a live process or permission to act.'

/**
 * The primary run header: objective, execution number, lifecycle, and the autonomous
 * session timer. Pause and Stop are rendered — not hidden — but disabled: this
 * increment does not implement their behavior, and the spec forbids implying that a
 * control works when it does not.
 */
export function RunHeader({ cockpit, planningSequenceControl, agentRun, hostObservation }: RunHeaderProps) {
  const recordedOnly = agentRun === true || (planningSequenceControl !== undefined && planningSequenceControl !== 'none')
  const isRunning = cockpit.lifecycle === 'Running' && !recordedOnly
  const autonomousSeconds = useTickingDuration(cockpit.autonomousDurationSeconds ?? 0, isRunning)

  return (
    <header className="dc-run-header">
      <div>
        <h1>{cockpit.objective}</h1>
        <div className="dc-run-header-meta">
          {cockpit.projectName} · Execution {cockpit.executionNumber}
        </div>
      </div>
      <span
        className="dc-badge"
        data-tone={recordedOnly && cockpit.lifecycle === 'Running' ? 'default' : (LIFECYCLE_TONE[cockpit.lifecycle ?? ''] ?? 'default')}
        data-recorded-lifecycle={recordedOnly ? 'true' : undefined}
        title={recordedOnly ? RECORDED_LIFECYCLE_TITLE : undefined}
      >
        {cockpit.lifecycle} · {cockpit.stage}
      </span>
      {hostObservation ? (
        <span className="dc-badge" data-testid="header-host-observation" title={HOST_OBSERVATION_TITLE}>
          {hostObservation}
        </span>
      ) : null}
      <span className="dc-badge" title={recordedOnly ? LIFECYCLE_ELAPSED_TITLE : AUTONOMOUS_TIME_TITLE}>
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
