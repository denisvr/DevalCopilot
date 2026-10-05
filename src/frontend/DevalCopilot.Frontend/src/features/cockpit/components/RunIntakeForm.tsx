import { useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { useRunIntake } from '../hooks/useRunIntake'
import {
  INITIAL_RUN_INTAKE_DRAFT,
  MAX_AGENT_CLAIMS,
  MAX_INVOCATION_MINUTES,
  MAX_OBJECTIVE_LENGTH,
  MIN_AGENT_CLAIMS,
  MIN_INVOCATION_MINUTES,
  RUN_INTAKE_BLOCKED_REASON,
  validateAgentClaimsDraft,
  validateInvocationMinutesDraft,
  validateRunIntakeDraft,
  validateRunObjective,
} from '../runIntake'
import type { RunIntakeDraft } from '../runIntake'

interface RunIntakeFormProps {
  projectId: string
  /** The server's hint that a new run may be created for this project (no run, or every run is
   * terminal). Never authoritative: the server re-checks on every submission. */
  canCreateRun: boolean
  /** Refreshes the project summaries; called after a current success or a blocked refusal. */
  onChanged: () => void
}

/**
 * Manual run intake for the selected project: a typed objective with two immutable run-wide budget
 * choices (Agent claims and reserved invocation minutes), plus a separate, clearly labelled
 * simulated demo action that ignores them. The objective and both budget drafts are ONE per-project
 * submission snapshot: every edit of any of them advances the project draft version, and a
 * successful submission resets the snapshot only if its version is still the one that was submitted,
 * so editing back to identical values does not restore the right to reset.
 * Pending, error, and success belong to the committed interaction of one project.
 */
export function RunIntakeForm({ projectId, canCreateRun, onChanged }: RunIntakeFormProps) {
  const [drafts, setDrafts] = useState<Record<string, RunIntakeDraft>>({})
  const draft = drafts[projectId] ?? INITIAL_RUN_INTAKE_DRAFT
  // Advanced synchronously by every edit; a completion may reset a draft only at the version it submitted.
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

  const validation = validateRunObjective(draft.objective)
  const claimsValidation = validateAgentClaimsDraft(draft.maximumAgentAttempts)
  const minutesValidation = validateInvocationMinutesDraft(draft.maximumAgentInvocationMinutes)
  const tooLong = draft.objective.length > MAX_OBJECTIVE_LENGTH

  function edit(patch: Partial<RunIntakeDraft>) {
    draftVersions.current[projectId] = (draftVersions.current[projectId] ?? 0) + 1
    setDrafts((current) => ({ ...current, [projectId]: { ...(current[projectId] ?? INITIAL_RUN_INTAKE_DRAFT), ...patch } }))
  }

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
              value={draft.objective}
              placeholder="Describe what this run should accomplish"
              onChange={(event) => edit({ objective: event.target.value })}
            />
            <span className="dc-run-intake-count" data-over={tooLong}>
              {draft.objective.length}/{MAX_OBJECTIVE_LENGTH}
            </span>
            {tooLong ? (
              <p className="dc-run-intake-validation" role="status">
                {validation}
              </p>
            ) : null}
            <div className="dc-run-intake-budgets">
              <label htmlFor="dc-run-max-claims">Agent claim ceiling</label>
              <input
                id="dc-run-max-claims"
                type="number"
                inputMode="numeric"
                min={MIN_AGENT_CLAIMS}
                max={MAX_AGENT_CLAIMS}
                step={1}
                value={draft.maximumAgentAttempts}
                aria-describedby="dc-run-budget-note"
                aria-invalid={claimsValidation !== null}
                onChange={(event) => edit({ maximumAgentAttempts: event.target.value })}
              />
              {claimsValidation ? (
                <p className="dc-run-intake-validation" role="status" data-testid="run-intake-claims-validation">
                  {claimsValidation}
                </p>
              ) : null}
              <label htmlFor="dc-run-max-minutes">Reserved invocation minutes</label>
              <input
                id="dc-run-max-minutes"
                type="number"
                inputMode="numeric"
                min={MIN_INVOCATION_MINUTES}
                max={MAX_INVOCATION_MINUTES}
                step={1}
                value={draft.maximumAgentInvocationMinutes}
                aria-describedby="dc-run-budget-note"
                aria-invalid={minutesValidation !== null}
                onChange={(event) => edit({ maximumAgentInvocationMinutes: event.target.value })}
              />
              {minutesValidation ? (
                <p className="dc-run-intake-validation" role="status" data-testid="run-intake-minutes-validation">
                  {minutesValidation}
                </p>
              ) : null}
              <p id="dc-run-budget-note" className="dc-run-intake-note" data-testid="run-intake-budget-note">
                These ceilings apply to the whole run. Agent claims stay consumed after a failure or interruption. Time is
                reserved from each attempt&apos;s configured timeout, not measured elapsed time. Both ceilings cannot be
                changed after the run is recorded, and a ceiling smaller than a role&apos;s configured timeout prevents that
                claim, which can be the first one.
              </p>
            </div>
            <button
              type="submit"
              className="dc-button"
              data-variant="primary"
              disabled={busy || validateRunIntakeDraft(draft) !== null}
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
              Demo only: a simulated run with a fixed objective and the default budgets. No Agent is invoked, and the
              objective and budget fields above are ignored.
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
