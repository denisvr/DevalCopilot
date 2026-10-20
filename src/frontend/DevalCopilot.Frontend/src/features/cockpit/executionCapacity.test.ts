import { describe, expect, it } from 'vitest'
import { describeGlobalAgentClaimBlock } from './deriveGlobalAgentClaimBlock'
import {
  EXECUTION_CAPACITY_CHANGE_FEEDBACK,
  EXECUTION_CAPACITY_GUIDANCE,
  EXECUTION_CAPACITY_STATUSES,
  EXECUTION_CAPACITY_WITHHELD_POINTER,
  deriveExecutionCapacityStartGate,
  describeExecutionCapacityChange,
  toExecutionCapacityReading,
} from './executionCapacity'
import type { ExecutionCapacityChange, ExecutionCapacityReadFailure, ExecutionCapacityState } from './executionCapacity'
import { capacityReadingOf, capacityStateFor, capacityWireFor } from './executionCapacityFixture'

const decode = (body: unknown) => toExecutionCapacityReading(body)

describe('toExecutionCapacityReading: the closed, whole-answer decoder', () => {
  it.each([
    ['Available', 0],
    ['Available', 1],
    ['AtCapacity', 2],
    ['AtCapacity', 3],
    ['AtCapacity', 2_147_483_647],
  ] as const)('accepts the exact %s tuple with %i recorded owners as the host wrote it', (status, owners) => {
    const body = capacityWireFor(status, {}, owners)

    expect(decode(body)).toEqual(body)
  })

  it('accepts the exact Unavailable tuple, which carries no count', () => {
    const body = capacityWireFor('Unavailable')

    expect(body.recordedOwners).toBeNull()
    expect(decode(body)).toEqual(body)
  })

  it.each([
    ['nothing', undefined],
    ['null', null],
    ['an array', []],
    ['an array of the answer', [capacityWireFor('Available')]],
    ['a string', 'Available'],
    ['a number', 1],
    ['an empty object', {}],
  ])('refuses %s', (_, body) => {
    expect(decode(body)).toBeNull()
  })

  it('refuses an unknown member', () => {
    expect(decode({ ...capacityWireFor('Available'), extra: 1 })).toBeNull()
    expect(decode({ ...capacityWireFor('Available'), projectId: '0a1b2c3d-1111-4111-8111-aaaaaaaaaaaa' })).toBeNull()
  })

  it.each(['limit', 'recordedOwners', 'status', 'reasonCode'])('refuses an answer whose %s key is missing, which is not an explicit null', (key) => {
    for (const status of EXECUTION_CAPACITY_STATUSES) {
      const { [key]: _removed, ...without } = capacityWireFor(status)
      expect(decode(without), `${status} without ${key}`).toBeNull()
    }
  })

  it.each([3, 0, -1, 2.5, 1.5, '2', '1', undefined, Number.NaN, Infinity, true, {}, []])(
    'refuses a limit of %j: it is exactly 1 or 2 (ADR-0059)',
    (limit) => {
      for (const status of EXECUTION_CAPACITY_STATUSES) {
        expect(decode(capacityWireFor(status, { limit })), status).toBeNull()
      }
    },
  )

  it('accepts a null limit only for Unavailable, where the configuration could not be validated, with a null count', () => {
    const unavailable = capacityWireFor('Unavailable', { limit: null })

    expect(decode(unavailable)).toEqual(unavailable)
    expect(decode(capacityWireFor('Available', { limit: null }))).toBeNull()
    expect(decode(capacityWireFor('AtCapacity', { limit: null }))).toBeNull()
    expect(decode(capacityWireFor('Unavailable', { limit: null, recordedOwners: 0 }))).toBeNull()
  })

  it.each([1, 2] as const)('accepts the exact Unavailable tuple with the validated limit %i', (limit) => {
    const body = capacityWireFor('Unavailable', {}, undefined, limit)

    expect(decode(body)).toEqual(body)
  })

  it.each([
    ['Available', 0, 1],
    ['AtCapacity', 1, 1],
    ['AtCapacity', 2, 1],
    ['AtCapacity', 2_147_483_647, 1],
  ] as const)('accepts the exact %s tuple with %i recorded owners at limit %i', (status, owners, limit) => {
    const body = capacityWireFor(status, {}, owners, limit)

    expect(decode(body)).toEqual(body)
  })

  it.each([
    ['Available', 1, 1],
    ['Available', 2, 1],
    ['AtCapacity', 0, 1],
    ['AtCapacity', 1, 2],
    ['Available', 2, 2],
    ['AtCapacity', 0, 2],
  ] as const)('refuses the %s tuple whose %i recorded owners contradict limit %i, never repairing it', (status, owners, limit) => {
    expect(decode(capacityWireFor(status, {}, owners, limit))).toBeNull()
  })

  it.each([-1, 2, 3, 1.5, 0.5, '0', '1', null, undefined, Number.NaN, Infinity, -Infinity, true, [], {}])(
    'refuses %j as the recorded owners of Available (0 or 1 only)',
    (recordedOwners) => {
      expect(decode(capacityWireFor('Available', { recordedOwners }))).toBeNull()
    },
  )

  it.each([-1, 0, 1, 2.5, 2_147_483_648, 3_000_000_000, '2', '3', null, undefined, Number.NaN, Infinity, true, [], {}])(
    'refuses %j as the recorded owners of AtCapacity (2 through Int32.MaxValue only)',
    (recordedOwners) => {
      expect(decode(capacityWireFor('AtCapacity', { recordedOwners }))).toBeNull()
    },
  )

  it.each([0, 1, 2, 3, '0', 'null', false, {}])('refuses %j as the recorded owners of Unavailable (an explicit null only)', (recordedOwners) => {
    expect(decode(capacityWireFor('Unavailable', { recordedOwners }))).toBeNull()
  })

  it.each([
    ['Available', 'execution_capacity_limit_reached'],
    ['Available', 'execution_capacity_unavailable'],
    ['Available', ''],
    ['AtCapacity', null],
    ['AtCapacity', 'execution_capacity_unavailable'],
    ['AtCapacity', 'execution_capacity.limit_reached'],
    ['AtCapacity', 'EXECUTION_CAPACITY_LIMIT_REACHED'],
    ['Unavailable', null],
    ['Unavailable', 'execution_capacity_limit_reached'],
    ['Unavailable', 'execution_capacity.unavailable'],
    ['Unavailable', 1],
  ] as const)('refuses the mismatched reason %j for %s', (status, reasonCode) => {
    expect(decode(capacityWireFor(status, { reasonCode }))).toBeNull()
  })

  it.each(['available', 'AVAILABLE', 'Idle', 'Full', ' Available', 'Available ', '', 'AtCapacity\u0000', null, 1, {}, undefined])(
    'refuses the status %j',
    (status) => {
      expect(decode(capacityWireFor('Available', { status }))).toBeNull()
    },
  )

  it('never repairs a refused answer: it returns null, not a corrected reading', () => {
    expect(decode(capacityWireFor('AtCapacity', { recordedOwners: 1 }))).toBeNull()
    expect(decode(capacityWireFor('Available', { recordedOwners: 2 }))).toBeNull()
  })
})

describe('deriveExecutionCapacityStartGate: only a validated Available allows, and it grants nothing by itself', () => {
  it('allows a mutating start only for a validated Available with 0 or 1 recorded owners', () => {
    expect(deriveExecutionCapacityStartGate(capacityStateFor('Available'))).toEqual({ allowsMutatingStart: true, guidance: null })
    const one: ExecutionCapacityState = { ...capacityStateFor('Available'), reading: capacityReadingOf('Available', 1) }
    expect(deriveExecutionCapacityStartGate(one)).toEqual({ allowsMutatingStart: true, guidance: null })
  })

  it.each([null, undefined])('withholds with the pending guidance when the state is absent (%s): an absent state is never a permission', (state) => {
    expect(deriveExecutionCapacityStartGate(state)).toEqual({ allowsMutatingStart: false, guidance: EXECUTION_CAPACITY_GUIDANCE.pending })
  })

  it('withholds with the pending guidance while nothing was validated', () => {
    expect(deriveExecutionCapacityStartGate(capacityStateFor(null, { loading: true }))).toEqual({
      allowsMutatingStart: false,
      guidance: EXECUTION_CAPACITY_GUIDANCE.pending,
    })
  })

  it.each(['AtCapacity', 'Unavailable'] as const)('withholds a validated %s with its own fixed guidance', (status) => {
    expect(deriveExecutionCapacityStartGate(capacityStateFor(status))).toEqual({
      allowsMutatingStart: false,
      guidance: EXECUTION_CAPACITY_GUIDANCE[status],
    })
  })

  it.each(['read', 'timeout', 'invalid'] as const)('withholds a %s failure with its own fixed guidance, even beside an earlier Available', (failure) => {
    expect(deriveExecutionCapacityStartGate(capacityStateFor(null, { failure }))).toEqual({
      allowsMutatingStart: false,
      guidance: EXECUTION_CAPACITY_GUIDANCE[failure],
    })
    expect(deriveExecutionCapacityStartGate(capacityStateFor('Available', { failure })).allowsMutatingStart).toBe(false)
  })

  it('words an unrecognized failure as the incoherent answer it is', () => {
    const state = capacityStateFor(null, { failure: 'toString' as ExecutionCapacityReadFailure })

    expect(deriveExecutionCapacityStartGate(state)).toEqual({ allowsMutatingStart: false, guidance: EXECUTION_CAPACITY_GUIDANCE.invalid })
  })

  it.each([
    ['an unknown status', { limit: 2, recordedOwners: 0, status: 'Idle', reasonCode: null }],
    ['a contradictory count', { limit: 2, recordedOwners: 2, status: 'Available', reasonCode: null }],
    ['a wrong limit', { limit: 3, recordedOwners: 0, status: 'Available', reasonCode: null }],
    ['a mismatched reason', { limit: 2, recordedOwners: 0, status: 'Available', reasonCode: 'execution_capacity_unavailable' }],
    ['an extra member', { limit: 2, recordedOwners: 0, status: 'Available', reasonCode: null, extra: true }],
  ])('never lets %s allow, and words it as incoherent', (_, reading) => {
    const state = { ...capacityStateFor(null), reading } as unknown as ExecutionCapacityState

    expect(deriveExecutionCapacityStartGate(state)).toEqual({ allowsMutatingStart: false, guidance: EXECUTION_CAPACITY_GUIDANCE.invalid })
  })

  it('carries only fixed English copy: no identifier, path, provider or number is taken from the answer', () => {
    for (const guidance of Object.values(EXECUTION_CAPACITY_GUIDANCE)) {
      expect(guidance).not.toMatch(/[0-9a-f]{8}-[0-9a-f]{4}|[A-Z]:\\|claude|codex/i)
      expect(guidance).toContain('Refresh')
    }

    const crowded = deriveExecutionCapacityStartGate(capacityStateFor('AtCapacity', { reading: capacityReadingOf('AtCapacity', 41) }))
    expect(crowded.guidance).toBe(EXECUTION_CAPACITY_GUIDANCE.AtCapacity)
    expect(crowded.guidance).not.toContain('41')
  })

  it('names what stays available, so withholding is never worded as blocking planning, review, authorization or inspection', () => {
    for (const guidance of Object.values(EXECUTION_CAPACITY_GUIDANCE)) {
      expect(guidance).toContain('Planning, review, diagnosis, authorizations, approvals')
      expect(guidance).toContain('Stop after current step')
    }
  })

  it('has a control pointer that is a fixed sentence and the claim-block copy of the capacity reason', () => {
    expect(EXECUTION_CAPACITY_WITHHELD_POINTER).toBe('Withheld: see the execution capacity guidance in this region.')
    expect(describeGlobalAgentClaimBlock({ reason: 'ExecutionCapacityWithheld' })).toBe(EXECUTION_CAPACITY_WITHHELD_POINTER)
  })
})

describe('deriveExecutionCapacityStartGate at the persisted limit (ADR-0059)', () => {
  it('allows a validated Available reading at limit one with no recorded owner, and nothing more', () => {
    expect(deriveExecutionCapacityStartGate(capacityStateFor('Available', {}, 1))).toEqual({ allowsMutatingStart: true, guidance: null })
  })

  it('words a validated AtCapacity at limit one with its own fixed guidance, never the limit-two wording', () => {
    const gate = deriveExecutionCapacityStartGate(capacityStateFor('AtCapacity', {}, 1))

    expect(gate).toEqual({ allowsMutatingStart: false, guidance: EXECUTION_CAPACITY_GUIDANCE.AtCapacityLimitOne })
    expect(gate.guidance).toMatch(/^One potentially mutating execution is already recorded/)
    expect(gate.guidance).not.toBe(EXECUTION_CAPACITY_GUIDANCE.AtCapacity)
    expect(EXECUTION_CAPACITY_GUIDANCE.AtCapacity).toMatch(/^Two potentially mutating executions are already recorded/)
  })

  it('words an AtCapacity limit-two reading exactly as before', () => {
    expect(deriveExecutionCapacityStartGate(capacityStateFor('AtCapacity', {}, 2)).guidance).toBe(EXECUTION_CAPACITY_GUIDANCE.AtCapacity)
  })

  it('judges the reading whole: a limit-one Available that records an owner is incoherent and withholds', () => {
    const state = { ...capacityStateFor(null), reading: { limit: 1, recordedOwners: 1, status: 'Available', reasonCode: null } } as unknown as ExecutionCapacityState

    expect(deriveExecutionCapacityStartGate(state)).toEqual({ allowsMutatingStart: false, guidance: EXECUTION_CAPACITY_GUIDANCE.invalid })
  })

  it.each(['applying', 'confirming'] as const)(
    'withholds every mutating start while a limit change is %s, even beside a validated Available reading',
    (phase) => {
      const state = capacityStateFor('Available', { change: { phase, outcome: null, requested: 1 } })

      expect(deriveExecutionCapacityStartGate(state)).toEqual({ allowsMutatingStart: false, guidance: EXECUTION_CAPACITY_GUIDANCE.changing })
    },
  )

  it('allows again once the change barrier is idle and a validated Available reading stands', () => {
    const state = capacityStateFor('Available', { change: { phase: 'idle', outcome: 'applied', requested: 1 } }, 1)

    expect(deriveExecutionCapacityStartGate(state)).toEqual({ allowsMutatingStart: true, guidance: null })
  })

  it('keeps the change guidance as fixed English that starts nothing and names what stays available', () => {
    expect(EXECUTION_CAPACITY_GUIDANCE.changing).toContain('recorded-execution limit')
    expect(EXECUTION_CAPACITY_GUIDANCE.changing).toContain('Planning, review, diagnosis, authorizations, approvals')
    expect(EXECUTION_CAPACITY_GUIDANCE.changing).toContain('Refresh')
  })
})

describe('describeExecutionCapacityChange: fixed English feedback that never implies a pending or inferred result', () => {
  const idle = (outcome: ExecutionCapacityChange['outcome'], requested: 1 | 2 | null = 1): ExecutionCapacityChange => ({
    phase: 'idle',
    outcome,
    requested,
  })

  it('says nothing before any change', () => {
    expect(describeExecutionCapacityChange(idle(null, null), capacityReadingOf('Available'))).toBeNull()
  })

  it('words applying and confirming without claiming a result', () => {
    expect(describeExecutionCapacityChange({ phase: 'applying', outcome: null, requested: 1 }, null)).toBe(EXECUTION_CAPACITY_CHANGE_FEEDBACK.applying)
    expect(describeExecutionCapacityChange({ phase: 'confirming', outcome: 'applied', requested: 1 }, null)).toBe(
      EXECUTION_CAPACITY_CHANGE_FEEDBACK.confirming,
    )
  })

  it('words a confirmed change with the limit that was read back, never the requested one', () => {
    expect(describeExecutionCapacityChange(idle('applied', 1), capacityReadingOf('Available', 0, 1))).toBe(
      EXECUTION_CAPACITY_CHANGE_FEEDBACK.appliedOne,
    )
    expect(describeExecutionCapacityChange(idle('applied', 2), capacityReadingOf('Available', 0, 2))).toBe(
      EXECUTION_CAPACITY_CHANGE_FEEDBACK.appliedTwo,
    )
  })

  it('does not claim success when the limit read back differs from the one requested', () => {
    expect(describeExecutionCapacityChange(idle('applied', 1), capacityReadingOf('Available', 0, 2))).toBe(
      EXECUTION_CAPACITY_CHANGE_FEEDBACK.differs,
    )
  })

  it('does not claim success without a validated read-back, whatever the write answered', () => {
    expect(describeExecutionCapacityChange(idle('applied', 1), null)).toBe(EXECUTION_CAPACITY_CHANGE_FEEDBACK.unconfirmed)
  })

  it.each(['belowOwners', 'refused', 'unconfirmed'] as const)('words %s with its own fixed copy', (outcome) => {
    expect(describeExecutionCapacityChange(idle(outcome), capacityReadingOf('Available'))).toBe(EXECUTION_CAPACITY_CHANGE_FEEDBACK[outcome])
  })

  it('states the lower-limit refusal truthfully: nothing changed and nothing is saved for later', () => {
    expect(EXECUTION_CAPACITY_CHANGE_FEEDBACK.belowOwners).toMatch(/nothing was changed/i)
    expect(EXECUTION_CAPACITY_CHANGE_FEEDBACK.belowOwners).toMatch(/not saved|nothing is saved|no pending/i)
    expect(EXECUTION_CAPACITY_CHANGE_FEEDBACK.unconfirmed).toMatch(/may or may not/i)
  })

  it('never claims that a change started, cancelled or released any work', () => {
    for (const text of Object.values(EXECUTION_CAPACITY_CHANGE_FEEDBACK)) {
      expect(text).not.toMatch(/\b(started|cancelled|released|queued|retried)\b/i)
    }
  })
})
