import { useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { useRunIntake } from '../hooks/useRunIntake'
import { MAX_OBJECTIVE_LENGTH, RUN_INTAKE_BLOCKED_REASON, validateRunObjective } from '../runIntake'

interface RunIntakeFormProps {
  projectId: string
  /** The server's hint that a new run may be created for this project (no run, or every run is
   * terminal). Never authoritative: the server re-checks on every submission. */
  canCreateRun: boolean
  /** Refreshes the project summaries; called after a current success or a blocked refusal. */
  onChanged: () => void
}

/**
 * Manual run intake for the selected project: a typed objective, plus a separate, clearly labelled
 * simulated demo action. Drafts are kept per project and never overwritten by a late continuation: every edit
 * advances the project draft version, and a successful submission clears the draft only if its version is still the one
 * that was submitted, so editing back to identical text does not restore the right to clear.
 * Pending, error, and success belong to the committed interaction of one project.
 */
export function RunIntakeForm({ projectId, canCreateRun, onChanged }: RunIntakeFormProps) {
  const [drafts, setDrafts] = useState<Record<string, string>>({})
  const draft = drafts[projectId] ?? ''
  // Advanced synchronously by every edit; a completion may clear a draft only at the version it submitted.
  const draftVersions = useRef<Record<string, number>>({})

  const { busy, pendingKind, error, created, createManual, startSimulated } = useRunIntake(projectId, {
    onManualCreated: (createdProjectId, submittedVersion) => {
      if ((draftVersions.current[createdProjectId] ?? 0) !== submittedVersion) {
        onChanged()
        return
      }
      setDrafts((current) => {
        const remaining = { ...current }
        delete remaining[createdProjectId]
        return remaining
      })
      onChanged()
    },
    onSimulatedStarted: onChanged,
    onBlocked: onChanged,
  })

  const validation = validateRunObjective(draft)
  const tooLong = draft.length > MAX_OBJECTIVE_LENGTH

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    await createManual(projectId, draft, draftVersions.current[projectId] ?? 0)
  }

  return (
    <section className="dc-run-intake" aria-label="Run intake">
      {created ? (
        <p className="dc-run-intake-created" role="status">
          {created.kind === 'manual'
            ? `Manual run recorded${created.executionNumber ? ` (execution ${created.executionNumber})` : ''}. It is shown below once the project list refreshes.`
            : 'Simulated demo run started. It is shown below once the project list refreshes.'}
        </p>
      ) : null}
      {canCreateRun ? (
        <>
          <form className="dc-run-intake-form" onSubmit={handleSubmit} aria-label="Record a manual run">
            <label htmlFor="dc-run-objective">Objective</label>
            <textarea
              id="dc-run-objective"
              rows={3}
              value={draft}
              placeholder="Describe what this run should accomplish"
              onChange={(event) => {
                const next = event.target.value
                draftVersions.current[projectId] = (draftVersions.current[projectId] ?? 0) + 1
                setDrafts((current) => ({ ...current, [projectId]: next }))
              }}
            />
            <span className="dc-run-intake-count" data-over={tooLong}>
              {draft.length}/{MAX_OBJECTIVE_LENGTH}
            </span>
            {tooLong ? (
              <p className="dc-run-intake-validation" role="status">
                {validation}
              </p>
            ) : null}
            <button
              type="submit"
              className="dc-button"
              data-variant="primary"
              disabled={busy || validation !== null}
            >
              {pendingKind === 'manual' ? 'Recording…' : 'Record manual run'}
            </button>
            <p className="dc-run-intake-note">
              Recording a run creates it in a waiting state. No Agent work starts until you request it.
            </p>
          </form>
          <div className="dc-run-intake-demo">
            <button
              type="button"
              className="dc-button"
              aria-describedby="dc-run-demo-caption"
              disabled={busy}
              onClick={() => void startSimulated(projectId)}
            >
              {pendingKind === 'simulated' ? 'Starting…' : 'Start simulated run'}
            </button>
            <span id="dc-run-demo-caption" className="dc-run-intake-demo-caption">
              Demo only: a simulated run with a fixed objective. No Agent is invoked.
            </span>
          </div>
        </>
      ) : (
        <p className="dc-run-intake-blocked" role="status">
          {RUN_INTAKE_BLOCKED_REASON}
        </p>
      )}
      {error ? (
        <p className="dc-run-intake-error" role="alert">
          {error}
        </p>
      ) : null}
    </section>
  )
}
