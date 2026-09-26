import { describe, expect, it } from 'vitest'
import { GetRunCockpitResponse } from '../../api/generated/api-client'
import { deriveOneAgentClaimSlotRemainingWarning } from './deriveOneAgentClaimSlotRemainingWarning'

const RUN_ID = 'run-1'

function cockpitFor(overrides: Partial<GetRunCockpitResponse>): GetRunCockpitResponse {
  return new GetRunCockpitResponse({
    runId: RUN_ID,
    maximumAgentAttempts: 16,
    agentAttemptsUsed: 15,
    agentBudgetExhausted: false,
    ...overrides,
  } as GetRunCockpitResponse)
}

describe('deriveOneAgentClaimSlotRemainingWarning', () => {
  it('shows the warning at the default 16-slot budget with exactly one slot remaining (15/16)', () => {
    expect(deriveOneAgentClaimSlotRemainingWarning(cockpitFor({}), RUN_ID)).toBe(true)
  })

  it('shows the warning at a historically raised maximum with one slot remaining (18/19)', () => {
    expect(
      deriveOneAgentClaimSlotRemainingWarning(
        cockpitFor({ maximumAgentAttempts: 19, agentAttemptsUsed: 18 }),
        RUN_ID,
      ),
    ).toBe(true)
  })

  it('shows the warning for the smallest possible budget with one slot remaining (0/1)', () => {
    expect(
      deriveOneAgentClaimSlotRemainingWarning(
        cockpitFor({ maximumAgentAttempts: 1, agentAttemptsUsed: 0 }),
        RUN_ID,
      ),
    ).toBe(true)
  })

  it('never shows the warning once the budget is fully exhausted (16/16), even though the exhausted flag lags behind', () => {
    expect(
      deriveOneAgentClaimSlotRemainingWarning(
        cockpitFor({ maximumAgentAttempts: 16, agentAttemptsUsed: 16, agentBudgetExhausted: false }),
        RUN_ID,
      ),
    ).toBe(false)
  })

  it('never shows the warning when the explicit exhausted flag is true, even if the count would otherwise read as one remaining', () => {
    expect(
      deriveOneAgentClaimSlotRemainingWarning(
        cockpitFor({ maximumAgentAttempts: 16, agentAttemptsUsed: 15, agentBudgetExhausted: true }),
        RUN_ID,
      ),
    ).toBe(false)
  })

  it('never shows the warning for more than one slot remaining', () => {
    expect(
      deriveOneAgentClaimSlotRemainingWarning(
        cockpitFor({ maximumAgentAttempts: 16, agentAttemptsUsed: 14 }),
        RUN_ID,
      ),
    ).toBe(false)
  })

  it('never shows the warning for an over-budget count', () => {
    expect(
      deriveOneAgentClaimSlotRemainingWarning(
        cockpitFor({ maximumAgentAttempts: 16, agentAttemptsUsed: 17, agentBudgetExhausted: false }),
        RUN_ID,
      ),
    ).toBe(false)
  })

  it('never shows the warning when the exhausted flag is missing (unknown), never treating a fallback false as trusted evidence', () => {
    expect(
      deriveOneAgentClaimSlotRemainingWarning(
        cockpitFor({ agentBudgetExhausted: undefined }),
        RUN_ID,
      ),
    ).toBe(false)
  })

  it('never shows the warning when maximumAgentAttempts is missing', () => {
    expect(
      deriveOneAgentClaimSlotRemainingWarning(cockpitFor({ maximumAgentAttempts: undefined }), RUN_ID),
    ).toBe(false)
  })

  it('never shows the warning when agentAttemptsUsed is missing', () => {
    expect(
      deriveOneAgentClaimSlotRemainingWarning(cockpitFor({ agentAttemptsUsed: undefined }), RUN_ID),
    ).toBe(false)
  })

  it('never shows the warning for a non-integer maximum or used count (malformed JSON crossing the API boundary)', () => {
    expect(
      deriveOneAgentClaimSlotRemainingWarning(
        cockpitFor({ maximumAgentAttempts: 16.5, agentAttemptsUsed: 15 }),
        RUN_ID,
      ),
    ).toBe(false)
    expect(
      deriveOneAgentClaimSlotRemainingWarning(
        cockpitFor({ maximumAgentAttempts: 16, agentAttemptsUsed: 15.5 }),
        RUN_ID,
      ),
    ).toBe(false)
  })

  it('never shows the warning for NaN or Infinity values', () => {
    expect(
      deriveOneAgentClaimSlotRemainingWarning(
        cockpitFor({ maximumAgentAttempts: Number.NaN, agentAttemptsUsed: 15 }),
        RUN_ID,
      ),
    ).toBe(false)
    expect(
      deriveOneAgentClaimSlotRemainingWarning(
        cockpitFor({ maximumAgentAttempts: Number.POSITIVE_INFINITY, agentAttemptsUsed: 15 }),
        RUN_ID,
      ),
    ).toBe(false)
  })

  it('never shows the warning for a non-positive maximum or a negative used count', () => {
    expect(
      deriveOneAgentClaimSlotRemainingWarning(cockpitFor({ maximumAgentAttempts: 0, agentAttemptsUsed: -1 }), RUN_ID),
    ).toBe(false)
    expect(
      deriveOneAgentClaimSlotRemainingWarning(cockpitFor({ maximumAgentAttempts: -16, agentAttemptsUsed: 15 }), RUN_ID),
    ).toBe(false)
    expect(
      deriveOneAgentClaimSlotRemainingWarning(cockpitFor({ agentAttemptsUsed: -1 }), RUN_ID),
    ).toBe(false)
  })

  it('never shows the warning for a null cockpit', () => {
    expect(deriveOneAgentClaimSlotRemainingWarning(null, RUN_ID)).toBe(false)
  })

  it('never shows the warning for a cockpit still describing a previously selected run', () => {
    expect(deriveOneAgentClaimSlotRemainingWarning(cockpitFor({ runId: 'run-0' }), RUN_ID)).toBe(false)
  })
})
