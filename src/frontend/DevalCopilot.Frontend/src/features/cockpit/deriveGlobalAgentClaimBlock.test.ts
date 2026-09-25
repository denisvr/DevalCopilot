import { describe, expect, it } from 'vitest'
import { GetRunCockpitResponse } from '../../api/generated/api-client'
import { deriveGlobalAgentClaimBlock, describeGlobalAgentClaimBlock } from './deriveGlobalAgentClaimBlock'

const RUN_ID = 'run-1'

function cockpitFor(overrides: Partial<GetRunCockpitResponse>): GetRunCockpitResponse {
  return new GetRunCockpitResponse({
    runId: RUN_ID,
    maximumAgentAttempts: 16,
    agentAttemptsUsed: 3,
    agentBudgetExhausted: false,
    agentInvocationTimeBudget: {
      maximumMilliseconds: 7_200_000,
      reservedMilliseconds: 1_200_000,
      remainingMilliseconds: 6_000_000,
      isLegacyUnknown: false,
      evidenceInvalid: false,
    },
    ...overrides,
  } as GetRunCockpitResponse)
}

describe('deriveGlobalAgentClaimBlock', () => {
  it('returns null (no known block) for a healthy run with room in both budgets', () => {
    expect(deriveGlobalAgentClaimBlock(cockpitFor({}), RUN_ID)).toBeNull()
  })

  it('blocks when the count budget is exhausted via the explicit flag', () => {
    const block = deriveGlobalAgentClaimBlock(cockpitFor({ agentBudgetExhausted: true }), RUN_ID)
    expect(block).toEqual({ reason: 'CountBudgetExhausted' })
  })

  it('blocks when used has reached maximum even if the explicit flag lags', () => {
    const block = deriveGlobalAgentClaimBlock(
      cockpitFor({ maximumAgentAttempts: 16, agentAttemptsUsed: 16, agentBudgetExhausted: false }),
      RUN_ID,
    )
    expect(block).toEqual({ reason: 'CountBudgetExhausted' })
  })

  it('blocks when the invocation-time budget remaining is exactly zero', () => {
    const block = deriveGlobalAgentClaimBlock(
      cockpitFor({
        agentInvocationTimeBudget: {
          maximumMilliseconds: 7_200_000,
          reservedMilliseconds: 7_200_000,
          remainingMilliseconds: 0,
          isLegacyUnknown: false,
          evidenceInvalid: false,
        },
      } as Partial<GetRunCockpitResponse>),
      RUN_ID,
    )
    expect(block).toEqual({ reason: 'TimeBudgetExhausted' })
  })

  it('blocks when the invocation-time budget remaining is negative', () => {
    const block = deriveGlobalAgentClaimBlock(
      cockpitFor({
        agentInvocationTimeBudget: {
          maximumMilliseconds: 7_200_000,
          reservedMilliseconds: 7_800_000,
          remainingMilliseconds: -600_000,
          isLegacyUnknown: false,
          evidenceInvalid: false,
        },
      } as Partial<GetRunCockpitResponse>),
      RUN_ID,
    )
    expect(block).toEqual({ reason: 'TimeBudgetExhausted' })
  })

  it('blocks when the invocation-time evidence is marked invalid', () => {
    const block = deriveGlobalAgentClaimBlock(
      cockpitFor({
        agentInvocationTimeBudget: {
          maximumMilliseconds: undefined,
          reservedMilliseconds: undefined,
          remainingMilliseconds: undefined,
          isLegacyUnknown: false,
          evidenceInvalid: true,
        },
      } as Partial<GetRunCockpitResponse>),
      RUN_ID,
    )
    expect(block).toEqual({ reason: 'TimeBudgetEvidenceInvalid' })
  })

  it('never blocks a legitimate legacy run with no time-budget policy at all', () => {
    const block = deriveGlobalAgentClaimBlock(
      cockpitFor({
        agentInvocationTimeBudget: {
          maximumMilliseconds: undefined,
          reservedMilliseconds: undefined,
          remainingMilliseconds: undefined,
          isLegacyUnknown: true,
          evidenceInvalid: false,
        },
      } as Partial<GetRunCockpitResponse>),
      RUN_ID,
    )
    expect(block).toBeNull()
  })

  it('a legacy run still blocks on a separately exhausted count budget', () => {
    const block = deriveGlobalAgentClaimBlock(
      cockpitFor({
        agentBudgetExhausted: true,
        agentInvocationTimeBudget: {
          maximumMilliseconds: undefined,
          reservedMilliseconds: undefined,
          remainingMilliseconds: undefined,
          isLegacyUnknown: true,
          evidenceInvalid: false,
        },
      } as Partial<GetRunCockpitResponse>),
      RUN_ID,
    )
    expect(block).toEqual({ reason: 'CountBudgetExhausted' })
  })

  it('never treats a positive-but-small remaining time as a block or as proof of sufficiency', () => {
    const block = deriveGlobalAgentClaimBlock(
      cockpitFor({
        agentInvocationTimeBudget: {
          maximumMilliseconds: 7_200_000,
          reservedMilliseconds: 7_199_999,
          remainingMilliseconds: 1,
          isLegacyUnknown: false,
          evidenceInvalid: false,
        },
      } as Partial<GetRunCockpitResponse>),
      RUN_ID,
    )
    expect(block).toBeNull()
  })

  it('treats a missing cockpit projection as unavailable and therefore blocked', () => {
    expect(deriveGlobalAgentClaimBlock(null, RUN_ID)).toEqual({ reason: 'BudgetProjectionUnavailable' })
  })

  it('treats a malformed projection (missing count-budget fields) as unavailable and blocked', () => {
    const block = deriveGlobalAgentClaimBlock(
      cockpitFor({ maximumAgentAttempts: undefined, agentAttemptsUsed: undefined, agentBudgetExhausted: undefined }),
      RUN_ID,
    )
    expect(block).toEqual({ reason: 'BudgetProjectionUnavailable' })
  })

  it('treats a missing time-budget object as unavailable and blocked', () => {
    const block = deriveGlobalAgentClaimBlock(cockpitFor({ agentInvocationTimeBudget: undefined }), RUN_ID)
    expect(block).toEqual({ reason: 'BudgetProjectionUnavailable' })
  })

  it('fails closed on a negative agentAttemptsUsed rather than reading it as headroom', () => {
    const block = deriveGlobalAgentClaimBlock(
      cockpitFor({ maximumAgentAttempts: 16, agentAttemptsUsed: -1, agentBudgetExhausted: false }),
      RUN_ID,
    )
    expect(block).toEqual({ reason: 'BudgetProjectionUnavailable' })
  })

  it('fails closed on a non-positive maximumAgentAttempts', () => {
    const block = deriveGlobalAgentClaimBlock(
      cockpitFor({ maximumAgentAttempts: 0, agentAttemptsUsed: 0, agentBudgetExhausted: false }),
      RUN_ID,
    )
    expect(block).toEqual({ reason: 'BudgetProjectionUnavailable' })
  })

  it('fails closed on a negative reservedMilliseconds paired with a formula-consistent but impossible positive remainder', () => {
    // Reserved must be a sum of non-negative reservations and can never be negative; the
    // Maximum - Reserved formula alone cannot catch this because -100_000 still arithmetically
    // produces the paired remaining figure below.
    const block = deriveGlobalAgentClaimBlock(
      cockpitFor({
        agentInvocationTimeBudget: {
          maximumMilliseconds: 7_200_000,
          reservedMilliseconds: -100_000,
          remainingMilliseconds: 7_300_000,
          isLegacyUnknown: false,
          evidenceInvalid: false,
        },
      } as Partial<GetRunCockpitResponse>),
      RUN_ID,
    )
    expect(block).toEqual({ reason: 'BudgetProjectionUnavailable' })
  })

  it('fails closed when remainingMilliseconds does not equal maximumMilliseconds minus reservedMilliseconds', () => {
    const block = deriveGlobalAgentClaimBlock(
      cockpitFor({
        agentInvocationTimeBudget: {
          maximumMilliseconds: 7_200_000,
          reservedMilliseconds: 1_200_000,
          remainingMilliseconds: 9_999_999,
          isLegacyUnknown: false,
          evidenceInvalid: false,
        },
      } as Partial<GetRunCockpitResponse>),
      RUN_ID,
    )
    expect(block).toEqual({ reason: 'BudgetProjectionUnavailable' })
  })

  it('fails closed on a non-positive maximumMilliseconds', () => {
    const block = deriveGlobalAgentClaimBlock(
      cockpitFor({
        agentInvocationTimeBudget: {
          maximumMilliseconds: 0,
          reservedMilliseconds: 0,
          remainingMilliseconds: 0,
          isLegacyUnknown: false,
          evidenceInvalid: false,
        },
      } as Partial<GetRunCockpitResponse>),
      RUN_ID,
    )
    expect(block).toEqual({ reason: 'BudgetProjectionUnavailable' })
  })

  it('still correctly returns null, untouched by the new coherence validation, for a legitimate legacy run', () => {
    const block = deriveGlobalAgentClaimBlock(
      cockpitFor({
        agentInvocationTimeBudget: {
          maximumMilliseconds: undefined,
          reservedMilliseconds: undefined,
          remainingMilliseconds: undefined,
          isLegacyUnknown: true,
          evidenceInvalid: false,
        },
      } as Partial<GetRunCockpitResponse>),
      RUN_ID,
    )
    expect(block).toBeNull()
  })

  it('still correctly returns null for a coherent positive-but-unproven-sufficient remaining time, not over-tightened by the new validation', () => {
    const block = deriveGlobalAgentClaimBlock(
      cockpitFor({
        agentInvocationTimeBudget: {
          maximumMilliseconds: 7_200_000,
          reservedMilliseconds: 7_199_999,
          remainingMilliseconds: 1,
          isLegacyUnknown: false,
          evidenceInvalid: false,
        },
      } as Partial<GetRunCockpitResponse>),
      RUN_ID,
    )
    expect(block).toBeNull()
  })

  it('fails closed on a non-integer (fractional) agentAttemptsUsed', () => {
    const block = deriveGlobalAgentClaimBlock(
      cockpitFor({ maximumAgentAttempts: 16, agentAttemptsUsed: 3.5, agentBudgetExhausted: false }),
      RUN_ID,
    )
    expect(block).toEqual({ reason: 'BudgetProjectionUnavailable' })
  })

  it('fails closed on a non-integer (fractional) maximumAgentAttempts', () => {
    const block = deriveGlobalAgentClaimBlock(
      cockpitFor({ maximumAgentAttempts: 16.25, agentAttemptsUsed: 3, agentBudgetExhausted: false }),
      RUN_ID,
    )
    expect(block).toEqual({ reason: 'BudgetProjectionUnavailable' })
  })

  it('fails closed on NaN or Infinity for either count-budget field', () => {
    expect(
      deriveGlobalAgentClaimBlock(
        cockpitFor({ maximumAgentAttempts: Number.NaN, agentAttemptsUsed: 3, agentBudgetExhausted: false }),
        RUN_ID,
      ),
    ).toEqual({ reason: 'BudgetProjectionUnavailable' })

    expect(
      deriveGlobalAgentClaimBlock(
        cockpitFor({ maximumAgentAttempts: 16, agentAttemptsUsed: Number.POSITIVE_INFINITY, agentBudgetExhausted: false }),
        RUN_ID,
      ),
    ).toEqual({ reason: 'BudgetProjectionUnavailable' })

    expect(
      deriveGlobalAgentClaimBlock(
        cockpitFor({ maximumAgentAttempts: Number.NEGATIVE_INFINITY, agentAttemptsUsed: 3, agentBudgetExhausted: false }),
        RUN_ID,
      ),
    ).toEqual({ reason: 'BudgetProjectionUnavailable' })
  })

  it('fails closed on an incoherent legacy-unknown shape carrying an unexpectedly populated maximumMilliseconds', () => {
    const block = deriveGlobalAgentClaimBlock(
      cockpitFor({
        agentInvocationTimeBudget: {
          maximumMilliseconds: 7_200_000,
          reservedMilliseconds: undefined,
          remainingMilliseconds: undefined,
          isLegacyUnknown: true,
          evidenceInvalid: false,
        },
      } as Partial<GetRunCockpitResponse>),
      RUN_ID,
    )
    expect(block).toEqual({ reason: 'BudgetProjectionUnavailable' })
  })

  it('fails closed on an incoherent legacy-unknown shape carrying an unexpectedly populated reservedMilliseconds', () => {
    const block = deriveGlobalAgentClaimBlock(
      cockpitFor({
        agentInvocationTimeBudget: {
          maximumMilliseconds: undefined,
          reservedMilliseconds: 1_200_000,
          remainingMilliseconds: undefined,
          isLegacyUnknown: true,
          evidenceInvalid: false,
        },
      } as Partial<GetRunCockpitResponse>),
      RUN_ID,
    )
    expect(block).toEqual({ reason: 'BudgetProjectionUnavailable' })
  })

  it('fails closed on an incoherent legacy-unknown shape carrying an unexpectedly populated remainingMilliseconds', () => {
    const block = deriveGlobalAgentClaimBlock(
      cockpitFor({
        agentInvocationTimeBudget: {
          maximumMilliseconds: undefined,
          reservedMilliseconds: undefined,
          remainingMilliseconds: 6_000_000,
          isLegacyUnknown: true,
          evidenceInvalid: false,
        },
      } as Partial<GetRunCockpitResponse>),
      RUN_ID,
    )
    expect(block).toEqual({ reason: 'BudgetProjectionUnavailable' })
  })

  it('re-confirms a genuinely well-formed legacy-unknown projection (all three time fields absent) still returns null with a healthy count budget', () => {
    const block = deriveGlobalAgentClaimBlock(
      cockpitFor({
        agentInvocationTimeBudget: {
          maximumMilliseconds: undefined,
          reservedMilliseconds: undefined,
          remainingMilliseconds: undefined,
          isLegacyUnknown: true,
          evidenceInvalid: false,
        },
      } as Partial<GetRunCockpitResponse>),
      RUN_ID,
    )
    expect(block).toBeNull()
  })

  it('accepts the proven one-millisecond truncation discrepancy (remaining exactly one less than maximum minus reserved) as coherent', () => {
    const block = deriveGlobalAgentClaimBlock(
      cockpitFor({
        agentInvocationTimeBudget: {
          maximumMilliseconds: 7_200_000,
          reservedMilliseconds: 1_200_000,
          // maximum - reserved = 6_000_000; this is exactly one less, the proven bound from
          // independently-truncated TimeSpan-to-millisecond conversion at the Api layer.
          remainingMilliseconds: 5_999_999,
          isLegacyUnknown: false,
          evidenceInvalid: false,
        },
      } as Partial<GetRunCockpitResponse>),
      RUN_ID,
    )
    expect(block).toBeNull()
  })

  it('fails closed when remaining is more than one millisecond below maximum minus reserved (beyond the proven truncation bound)', () => {
    const block = deriveGlobalAgentClaimBlock(
      cockpitFor({
        agentInvocationTimeBudget: {
          maximumMilliseconds: 7_200_000,
          reservedMilliseconds: 1_200_000,
          // maximum - reserved = 6_000_000; two less than that exceeds the proven one-tick bound.
          remainingMilliseconds: 5_999_998,
          isLegacyUnknown: false,
          evidenceInvalid: false,
        },
      } as Partial<GetRunCockpitResponse>),
      RUN_ID,
    )
    expect(block).toEqual({ reason: 'BudgetProjectionUnavailable' })
  })

  it('fails closed when remaining exceeds maximum minus reserved (the discrepancy can never run in this direction)', () => {
    const block = deriveGlobalAgentClaimBlock(
      cockpitFor({
        agentInvocationTimeBudget: {
          maximumMilliseconds: 7_200_000,
          reservedMilliseconds: 1_200_000,
          // maximum - reserved = 6_000_000; one MORE than that is never a valid independently-
          // truncated shape, since Remaining can only ever be equal to or one less than the diff.
          remainingMilliseconds: 6_000_001,
          isLegacyUnknown: false,
          evidenceInvalid: false,
        },
      } as Partial<GetRunCockpitResponse>),
      RUN_ID,
    )
    expect(block).toEqual({ reason: 'BudgetProjectionUnavailable' })
  })

  it('never uses a previous run\'s projection for the currently selected run', () => {
    const staleProjection = cockpitFor({ runId: 'previous-run', agentBudgetExhausted: true })
    const block = deriveGlobalAgentClaimBlock(staleProjection, RUN_ID)
    expect(block).toEqual({ reason: 'BudgetProjectionUnavailable' })
  })
})

describe('describeGlobalAgentClaimBlock', () => {
  it('always attributes the block to the global run-wide budget, never a role', () => {
    expect(describeGlobalAgentClaimBlock({ reason: 'CountBudgetExhausted' })).toMatch(/run-wide agent claim budget/i)
    expect(describeGlobalAgentClaimBlock({ reason: 'TimeBudgetExhausted' })).toMatch(/invocation-time budget/i)
    expect(describeGlobalAgentClaimBlock({ reason: 'TimeBudgetEvidenceInvalid' })).toMatch(/invocation-time evidence/i)
    expect(describeGlobalAgentClaimBlock({ reason: 'BudgetProjectionUnavailable' })).toMatch(/budget status is confirmed/i)
  })
})
