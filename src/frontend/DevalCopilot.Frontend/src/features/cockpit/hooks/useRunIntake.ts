import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'
import { createManualRunClient, startSimulatedRunClient } from '../../../api/clients'
import { CreateManualRunRequest, StartSimulatedRunRequest } from '../../../api/generated/api-client'
import type { CreateManualRunResponse } from '../../../api/generated/api-client'
import { describeRunIntakeFailure, isRunIntakeBlocked, validateRunObjective } from '../runIntake'
import { useRunScopedAction } from './useRunScopedAction'

/** The fixed objective of the labelled demo action; it is never presented as a user objective. */
export const SIMULATED_DEMO_OBJECTIVE = 'Prove the walking skeleton'

const INTAKE_CONTROL = 'run-intake'

export type RunIntakeKind = 'manual' | 'simulated'

export interface CreatedRunNotice {
  projectId: string
  kind: RunIntakeKind
  executionNumber?: number
}

export interface UseRunIntakeOptions {
  /** Invoked once, only for a current success, with the draft version that was submitted. */
  onManualCreated: (projectId: string, submittedDraftVersion: number) => void
  onSimulatedStarted: () => void
  /** Invoked when the server reported the project already has an unfinished run and the
   * interaction is still current, so the caller can refresh its (hint-only) availability. */
  onBlocked: () => void
}

export interface RunIntake {
  busy: boolean
  pendingKind: RunIntakeKind | null
  error: string | null
  created: CreatedRunNotice | null
  createManual: (projectId: string, objective: string, draftVersion: number) => Promise<boolean>
  startSimulated: (projectId: string) => Promise<boolean>
}

/**
 * Run intake for one project: a manual objective submission and the labelled simulated demo.
 * Pending, error, and success are owned by the committed interaction lifetime of `projectId`
 * (useRunScopedAction): switching projects or unmounting drops them, and a late completion of an
 * ended lifetime neither writes state, refreshes, nor clears a draft. The two submissions share one
 * control, so a second submission in the same tick, of either kind, is rejected synchronously
 * without sending a request. An obsolete completion never undoes or repeats a request the server
 * already accepted; the next authoritative read shows its real outcome.
 */
export function useRunIntake(projectId: string, options: UseRunIntakeOptions): RunIntake {
  const { busy, error, run, reportError, capture } = useRunScopedAction(projectId)
  const [created, setCreated] = useState<CreatedRunNotice | null>(null)
  const [pendingKind, setPendingKind] = useState<RunIntakeKind | null>(null)
  const optionsRef = useRef(options)
  useLayoutEffect(() => {
    optionsRef.current = options
  })

  useEffect(
    () => () => {
      setCreated(null)
      setPendingKind(null)
    },
    [projectId],
  )

  const submit = useCallback(
    async (
      requestProjectId: string,
      kind: RunIntakeKind,
      send: () => Promise<CreateManualRunResponse | unknown>,
      fallback: string,
      onSuccess: () => void,
    ) => {
      const stillCurrent = capture()
      let failure: unknown = null
      let executionNumber: number | undefined
      const accepted = await run(
        requestProjectId,
        async () => {
          setPendingKind(kind)
          setCreated(null)
          try {
            const result = (await send()) as { executionNumber?: number } | null
            executionNumber = result?.executionNumber
          } catch (caught: unknown) {
            failure = caught
            throw caught
          }
        },
        {
          control: INTAKE_CONTROL,
          toMessage: (caught) => describeRunIntakeFailure(caught, fallback),
          onSuccess: () => {
            setPendingKind(null)
            setCreated({ projectId: requestProjectId, kind, executionNumber })
            onSuccess()
          },
        },
      )
      if (!accepted && failure && isRunIntakeBlocked(failure) && stillCurrent()) {
        optionsRef.current.onBlocked()
      }
      return accepted
    },
    [run, capture],
  )

  const createManual = useCallback(
    (requestProjectId: string, objective: string, draftVersion: number) => {
      const invalid = validateRunObjective(objective)
      if (invalid) {
        reportError(invalid)
        return Promise.resolve(false)
      }
      return submit(
        requestProjectId,
        'manual',
        () =>
          createManualRunClient().createManualRun(
            new CreateManualRunRequest({ projectId: requestProjectId, objective }),
          ),
        'The run could not be created.',
        () => optionsRef.current.onManualCreated(requestProjectId, draftVersion),
      )
    },
    [submit, reportError],
  )

  const startSimulated = useCallback(
    (requestProjectId: string) =>
      submit(
        requestProjectId,
        'simulated',
        () =>
          startSimulatedRunClient().startSimulatedRun(
            new StartSimulatedRunRequest({ projectId: requestProjectId, objective: SIMULATED_DEMO_OBJECTIVE }),
          ),
        'The simulated demo run could not be started.',
        () => optionsRef.current.onSimulatedStarted(),
      ),
    [submit],
  )

  return {
    busy,
    pendingKind: busy ? pendingKind : null,
    error,
    created: created?.projectId === projectId ? created : null,
    createManual,
    startSimulated,
  }
}
