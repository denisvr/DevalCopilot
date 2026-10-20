import { describe, expect, it } from 'vitest'
import { describeGlobalAgentClaimBlock } from './deriveGlobalAgentClaimBlock'
import {
  EXECUTION_CAPACITY_GUIDANCE,
  EXECUTION_CAPACITY_STATUSES,
  EXECUTION_CAPACITY_WITHHELD_POINTER,
  deriveExecutionCapacityStartGate,
  toExecutionCapacityReading,
} from './executionCapacity'
import type { ExecutionCapacityReadFailure, ExecutionCapacityState } from './executionCapacity'
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

  it.each([1, 3, 0, -1, 2.5, '2', null, undefined, Number.NaN, Infinity, true, {}])('refuses a limit of %j: it is always exactly 2', (limit) => {
    for (const status of EXECUTION_CAPACITY_STATUSES) {
      expect(decode(capacityWireFor(status, { limit })), status).toBeNull()
    }
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
