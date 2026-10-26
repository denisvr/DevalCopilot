import { useCallback, useEffect, useLayoutEffect, useRef } from 'react'
import { planningSequenceInspectionIndexClient, planningSequenceInspectionStepClient } from '../../../api/clients'
import { toPlanningInspectionIndex, toPlanningStepAnswer } from '../planningSequenceInspection'
import type { PlanningInspectionIndex, PlanningInspectionStep, PlanningStepAnswer } from '../planningSequenceInspection'
import { useOwnedLifetime, useOwnedState } from './useOwnedLifetime'
import type { OwnedLifetime } from './useOwnedLifetime'

/** `read`: the request failed. `not-found`: the host no longer knows this run or step. `invalid`: the answer is not coherent for the request. */
export type PlanningSequenceInspectionFailure = 'read' | 'not-found' | 'invalid'

export interface UsePlanningSequenceInspectionResult {
  /** Whether the region of the current run is open. */
  open: boolean
  /** The lifetime of the current index visit (null while closed): every step and attempt below belongs to it. */
  visitKey: string | null
  pending: boolean
  /** The validated index of this visit; never together with a failure. */
  index: PlanningInspectionIndex | null
  failure: PlanningSequenceInspectionFailure | null
  /** Opens the region and reads the index once; an open region is left as it is. */
  show: () => void
  /** Closes the opening that created this callback, discarding its index, selection, association and any read in flight. */
  close: () => void
  /** Reads the index again as a new visit: the selection and every association of the earlier visit end before the read starts. */
  reload: () => void
  /** The step chosen in this visit, as the accepted index shows it, or null. */
  selectedStep: PlanningInspectionStep | null
  /** The lifetime of the chosen step visit (null: none), for the per-attempt detail it owns. */
  stepKey: string | null
  stepPending: boolean
  stepAnswer: PlanningStepAnswer | null
  stepFailure: PlanningSequenceInspectionFailure | null
  /** Chooses one step of the accepted index and reads only that association; an unknown step is ignored. */
  selectStep: (stepId: string) => void
  /** Clears the choice of the step visit that created this callback; a later visit is never cleared by it. */
  clearStep: () => void
  /** Reads the same step again after a failure of this step visit; nothing else repeats a read. */
  retryStep: () => void
}

interface OpeningFrame {
  open: boolean
  serial: number
  visit: number
}

interface IndexFrame {
  pending: boolean
  index: PlanningInspectionIndex | null
  failure: PlanningSequenceInspectionFailure | null
}

interface StepChoiceFrame {
  chosen: { stepId: string; visit: number } | null
  visits: number
}

interface StepFrame {
  pending: boolean
  answer: PlanningStepAnswer | null
  failure: PlanningSequenceInspectionFailure | null
}

const createClosed = (): OpeningFrame => ({ open: false, serial: 0, visit: 0 })
const createPendingIndex = (): IndexFrame => ({ pending: true, index: null, failure: null })
const createNoStep = (): StepChoiceFrame => ({ chosen: null, visits: 0 })
const createPendingStep = (): StepFrame => ({ pending: true, answer: null, failure: null })

const isNotFound = (caught: unknown): boolean =>
  typeof caught === 'object' && caught !== null && (caught as { status?: unknown }).status === 404

/**
 * The explicit recorded inspection of one run's planning sequence (ADR-0068): its index and one chosen step's attempt association.
 * Lifetimes nest and each owns its callbacks and results: the run, each opening, each index visit (an open or a reload) and each chosen
 * step visit (which owns its association read and the Agent detail mounted under it). A replacement derives its first frame in the same
 * render, so another run, a return to an earlier one (A-B-A), closing, reopening, a reload or unmounting can never show or settle
 * anything of an earlier lifetime, and a retained callback of an ended lifetime starts and clears nothing. A reload ends the selection
 * and its content in the same render that starts the read, so nothing of the earlier visit is shown while the new answer is pending.
 *
 * Nothing is read until the owner opens the region; choosing a step reads only that step. Nothing is polled, retried automatically,
 * prefetched, cached across lifetimes, logged, stored or exported, and nothing here sends a command or touches the live planning reader.
 */
export function usePlanningSequenceInspection(runId: string | null): UsePlanningSequenceInspectionResult {
  const runOwner = useOwnedLifetime(runId)
  const [opening, commitOpening] = useOwnedState(runOwner, createClosed)
  const isOpen = runId !== null && opening.open
  const visitKey = isOpen ? JSON.stringify([runId, opening.serial, opening.visit]) : null
  const indexOwner = useOwnedLifetime(visitKey)
  const [indexFrame, commitIndex] = useOwnedState(indexOwner, createPendingIndex)
  const [stepChoice, commitStepChoice] = useOwnedState(indexOwner, createNoStep)

  const displayedIndex = useRef<{ owner: OwnedLifetime; frame: IndexFrame } | null>(null)
  useLayoutEffect(() => {
    displayedIndex.current = { owner: indexOwner, frame: indexFrame }
  })

  const show = useCallback(() => {
    if (!runOwner.isActive() || runOwner.key === null) {
      return
    }

    commitOpening((previous) => (previous.open ? previous : { open: true, serial: previous.serial + 1, visit: 1 }))
  }, [runOwner, commitOpening])

  const { serial, visit } = opening
  const close = useCallback(() => {
    if (!indexOwner.isActive() || indexOwner.key === null) {
      return
    }

    commitOpening((previous) => (previous.serial === serial && previous.open ? { ...previous, open: false } : previous))
  }, [indexOwner, serial, commitOpening])

  const reload = useCallback(() => {
    if (!indexOwner.isActive() || indexOwner.key === null) {
      return
    }

    // Decided against the queued value: only the visit this callback was rendered for is replaced, once.
    commitOpening((previous) =>
      previous.open && previous.serial === serial && previous.visit === visit ? { ...previous, visit: previous.visit + 1 } : previous,
    )
  }, [indexOwner, serial, visit, commitOpening])

  useEffect(() => {
    if (indexOwner.key === null || runId === null) {
      return
    }

    const isCurrent = indexOwner.begin('index')
    const settle = (frame: IndexFrame) => {
      if (isCurrent()) {
        commitIndex(() => frame)
      }
    }

    commitIndex(createPendingIndex)
    planningSequenceInspectionIndexClient()
      .readIndex(runId)
      .then(
        (answer) => {
          const accepted = toPlanningInspectionIndex(answer, runId)
          settle(accepted === null ? { pending: false, index: null, failure: 'invalid' } : { pending: false, index: accepted, failure: null })
        },
        (caught: unknown) => settle({ pending: false, index: null, failure: isNotFound(caught) ? 'not-found' : 'read' }),
      )
  }, [indexOwner, runId, commitIndex])

  const index = isOpen ? indexFrame.index : null
  const steps = index?.status === 'Recorded' ? index.steps : null

  const selectStep = useCallback(
    (stepId: string) => {
      const current = displayedIndex.current
      if (!indexOwner.isActive() || indexOwner.key === null || current === null || current.owner !== indexOwner) {
        return
      }

      const known = current.frame.index?.status === 'Recorded' && current.frame.index.steps.some((step) => step.stepId === stepId)
      if (!known) {
        return
      }

      // Decided against the queued choice, so a Hide queued earlier in the same batch makes this a new visit.
      commitStepChoice((previous) =>
        previous.chosen?.stepId === stepId ? previous : { chosen: { stepId, visit: previous.visits + 1 }, visits: previous.visits + 1 },
      )
    },
    [indexOwner, commitStepChoice],
  )

  const chosenStep = isOpen && steps !== null ? stepChoice.chosen : null
  const selectedStep = chosenStep === null || steps === null ? null : (steps.find((step) => step.stepId === chosenStep.stepId) ?? null)
  const stepVisit = selectedStep === null ? null : (chosenStep?.visit ?? null)
  const stepKey = selectedStep === null || visitKey === null ? null : JSON.stringify([visitKey, selectedStep.stepId, stepVisit])
  const stepOwner = useOwnedLifetime(stepKey)
  const [stepFrame, commitStep] = useOwnedState(stepOwner, createPendingStep)

  const clearStep = useCallback(() => {
    if (stepVisit === null || !indexOwner.isActive() || indexOwner.key === null) {
      return
    }

    commitStepChoice((previous) => (previous.chosen?.visit === stepVisit ? { ...previous, chosen: null } : previous))
  }, [indexOwner, stepVisit, commitStepChoice])

  const displayedStep = useRef<{ owner: OwnedLifetime; frame: StepFrame } | null>(null)
  const inFlight = useRef<{ owner: OwnedLifetime; isCurrent: () => boolean } | null>(null)
  const indexedStep = useRef(selectedStep)
  useLayoutEffect(() => {
    displayedStep.current = { owner: stepOwner, frame: stepFrame }
    indexedStep.current = selectedStep
  })

  const readStep = useCallback(() => {
    const target = indexedStep.current
    const header = displayedIndex.current?.frame.index
    if (!stepOwner.isActive() || stepOwner.key === null || runId === null || target === null || header?.status !== 'Recorded') {
      return
    }

    const sequenceId = header.sequence.sequenceId
    const isCurrent = stepOwner.begin('step')
    inFlight.current = { owner: stepOwner, isCurrent }
    const settle = (frame: StepFrame) => {
      if (isCurrent()) {
        inFlight.current = null
        commitStep(() => frame)
      }
    }

    commitStep(createPendingStep)
    planningSequenceInspectionStepClient()
      .readStep(runId, target.stepId)
      .then(
        (answer) => {
          const decided = toPlanningStepAnswer(answer, { runId, sequenceId, step: target })
          settle(
            decided.kind === 'incoherent'
              ? { pending: false, answer: null, failure: 'invalid' }
              : { pending: false, answer: decided, failure: null },
          )
        },
        (caught: unknown) => settle({ pending: false, answer: null, failure: isNotFound(caught) ? 'not-found' : 'read' }),
      )
  }, [stepOwner, runId, commitStep])

  useEffect(() => {
    readStep()
  }, [readStep])

  const retryStep = useCallback(() => {
    const current = displayedStep.current
    if (!stepOwner.isActive() || stepOwner.key === null || current === null || current.owner !== stepOwner) {
      return
    }

    if (current.frame.failure === null || current.frame.pending || (inFlight.current?.owner === stepOwner && inFlight.current.isCurrent())) {
      return
    }

    readStep()
  }, [stepOwner, readStep])

  return {
    open: isOpen,
    visitKey,
    pending: isOpen && indexFrame.pending,
    index,
    failure: isOpen ? indexFrame.failure : null,
    show,
    close,
    reload,
    selectedStep,
    stepKey,
    stepPending: selectedStep !== null && stepFrame.pending,
    stepAnswer: selectedStep === null ? null : stepFrame.answer,
    stepFailure: selectedStep === null ? null : stepFrame.failure,
    selectStep,
    clearStep,
    retryStep,
  }
}
