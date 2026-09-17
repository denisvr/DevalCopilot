import { useRunCockpit } from '../hooks/useRunCockpit'
import { useCollaborationTimeline } from '../hooks/useCollaborationTimeline'
import { selectCurrentProcessAttemptId } from '../selectCurrentProcessAttempt'
import { AgentCollaboration } from './AgentCollaboration'
import { ConnectionBanner } from './ConnectionBanner'
import { LiveOutputDrawer } from './LiveOutputDrawer'
import { RunHeader } from './RunHeader'
import { UsageEvidenceRail } from './UsageEvidenceRail'
import { WorkflowRail } from './WorkflowRail'

interface RunCockpitViewProps {
  runId: string
}

export function RunCockpitView({ runId }: RunCockpitViewProps) {
  const { cockpit, cards, connection, loading, error, syncError } = useRunCockpit(runId)
  const collaborationTimeline = useCollaborationTimeline(runId, cockpit?.latestSequence)

  if (loading && !cockpit) {
    return <p className="dc-empty-state">Loading run…</p>
  }

  if (error) {
    return <p className="dc-empty-state">{error}</p>
  }

  if (!cockpit) {
    return <p className="dc-empty-state">This run could not be found.</p>
  }

  const currentProcessAttemptId = selectCurrentProcessAttemptId(cards)

  return (
    <>
      <ConnectionBanner state={connection} syncError={syncError} />
      <RunHeader cockpit={cockpit} />
      <div className="dc-workspace">
        <WorkflowRail stageMap={cockpit.stageMap ?? []} />
        <AgentCollaboration {...collaborationTimeline} />
        <UsageEvidenceRail />
      </div>
      <LiveOutputDrawer runId={runId} attemptId={currentProcessAttemptId} />
    </>
  )
}
