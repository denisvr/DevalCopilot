import { describe, expect, it } from 'vitest'
import {
  AgentClaimPathTimeFitResponse,
  AgentInvocationTimeBudgetResponse,
  GetRunCockpitResponse,
} from '../../api/generated/api-client'
import {
  deriveAgentClaimPathTimeFit,
  describeAgentClaimPathTimeFitBlock,
  isAgentClaimPathTimeFitBlocking,
} from './deriveAgentClaimPathTimeFit'

const RUN_ID = 'run-1'

const CLAIM_PATHS = ['CodexPlanning', 'ClaudeCriticalReview', 'ChallengeResolution', 'Implementation', 'CodeReview', 'ReviewCorrection']

// A healthy, non-legacy, evidence-valid time budget — the coherent default every test below
// starts from unless it is specifically exercising a legacy/evidence-invalid/contradictory case.
// No role/provider fields: the fit DTO carries only claimPath and fit (the six action components
// already know their own claim path statically, so the server never repeats role/provider here).
function healthyBudget() {
  return new AgentInvocationTimeBudgetResponse({
    maximumMilliseconds: 7_200_000,
    reservedMilliseconds: 600_000,
    remainingMilliseconds: 6_600_000,
    isLegacyUnknown: false,
    evidenceInvalid: false,
  })
}

function legacyBudget() {
  return new AgentInvocationTimeBudgetResponse({
    maximumMilliseconds: undefined,
    reservedMilliseconds: undefined,
    remainingMilliseconds: undefined,
    isLegacyUnknown: true,
    evidenceInvalid: false,
  })
}

function evidenceInvalidBudget() {
  return new AgentInvocationTimeBudgetResponse({
    maximumMilliseconds: 7_200_000,
    reservedMilliseconds: undefined,
    remainingMilliseconds: undefined,
    isLegacyUnknown: false,
    evidenceInvalid: true,
  })
}

function fit(claimPath: string, value: string) {
  return new AgentClaimPathTimeFitResponse({ claimPath, fit: value })
}

function sixFits(overrideClaimPath?: string, overrideFit?: string) {
  return CLAIM_PATHS.map((path) => fit(path, path === overrideClaimPath ? overrideFit! : 'Fits'))
}

function cockpitFor(overrides: Partial<GetRunCockpitResponse>): GetRunCockpitResponse {
  return new GetRunCockpitResponse({
    runId: RUN_ID,
    agentClaimPathTimeFits: sixFits(),
    agentInvocationTimeBudget: healthyBudget(),
    ...overrides,
  } as GetRunCockpitResponse)
}

describe('deriveAgentClaimPathTimeFit', () => {
  it('returns Fits when the backend reports Fits for this claim path and the run-wide budget is healthy', () => {
    const result = deriveAgentClaimPathTimeFit(cockpitFor({}), RUN_ID, 'CodexPlanning')
    expect(result).toEqual({ reason: 'Fits' })
    expect(isAgentClaimPathTimeFitBlocking(result)).toBe(false)
    expect(describeAgentClaimPathTimeFitBlock(result)).toBeNull()
  })

  it('returns DoesNotFit when the backend reports DoesNotFit for this claim path, and it blocks', () => {
    const cockpit = cockpitFor({ agentClaimPathTimeFits: sixFits('Implementation', 'DoesNotFit') })
    const result = deriveAgentClaimPathTimeFit(cockpit, RUN_ID, 'Implementation')
    expect(result).toEqual({ reason: 'DoesNotFit' })
    expect(isAgentClaimPathTimeFitBlocking(result)).toBe(true)
    expect(describeAgentClaimPathTimeFitBlock(result)).toMatch(/insufficient reserved invocation time/i)
  })

  it('the ten-minute split example: exactly 10 minutes remaining fits every 10-minute path and rejects every 20-minute path', () => {
    const cockpit = cockpitFor({
      agentClaimPathTimeFits: [
        fit('CodexPlanning', 'Fits'),
        fit('ClaudeCriticalReview', 'Fits'),
        fit('ChallengeResolution', 'Fits'),
        fit('Implementation', 'DoesNotFit'),
        fit('CodeReview', 'Fits'),
        fit('ReviewCorrection', 'DoesNotFit'),
      ],
    })

    expect(deriveAgentClaimPathTimeFit(cockpit, RUN_ID, 'CodexPlanning').reason).toBe('Fits')
    expect(deriveAgentClaimPathTimeFit(cockpit, RUN_ID, 'ClaudeCriticalReview').reason).toBe('Fits')
    expect(deriveAgentClaimPathTimeFit(cockpit, RUN_ID, 'ChallengeResolution').reason).toBe('Fits')
    expect(deriveAgentClaimPathTimeFit(cockpit, RUN_ID, 'CodeReview').reason).toBe('Fits')
    expect(deriveAgentClaimPathTimeFit(cockpit, RUN_ID, 'Implementation').reason).toBe('DoesNotFit')
    expect(deriveAgentClaimPathTimeFit(cockpit, RUN_ID, 'ReviewCorrection').reason).toBe('DoesNotFit')
  })

  it('a candidate timeout exactly equal to the remaining time still fits (never blocked at the exact boundary)', () => {
    // The boundary itself is computed server-side; the frontend only ever reads the closed
    // "Fits" string the backend already decided for the exact-equality case.
    const cockpit = cockpitFor({ agentClaimPathTimeFits: sixFits('CodexPlanning', 'Fits') })
    const result = deriveAgentClaimPathTimeFit(cockpit, RUN_ID, 'CodexPlanning')
    expect(result.reason).toBe('Fits')
    expect(isAgentClaimPathTimeFitBlocking(result)).toBe(false)
  })

  it('LegacyUnknown never blocks when the run-wide budget agrees this run is legacy-unknown', () => {
    // A genuinely legacy run reports LegacyUnknown for every claim path — never mixed with Fits
    // or DoesNotFit, matching what `RunCockpitAgentClaimPathTimeFitProjection.Compute` actually
    // produces for `RunCockpitAgentInvocationTimeBudgetSummary.LegacyUnknown()`.
    const legacyCockpit = cockpitFor({
      agentClaimPathTimeFits: CLAIM_PATHS.map((path) => fit(path, 'LegacyUnknown')),
      agentInvocationTimeBudget: legacyBudget(),
    })
    const result = deriveAgentClaimPathTimeFit(legacyCockpit, RUN_ID, 'CodexPlanning')
    expect(result).toEqual({ reason: 'LegacyUnknown' })
    expect(isAgentClaimPathTimeFitBlocking(result)).toBe(false)
    expect(describeAgentClaimPathTimeFitBlock(result)).toBeNull()
  })

  it('EvidenceInvalid blocks and is described distinctly from DoesNotFit when the run-wide budget agrees evidence is invalid', () => {
    const cockpit = cockpitFor({
      agentClaimPathTimeFits: CLAIM_PATHS.map((path) => fit(path, 'EvidenceInvalid')),
      agentInvocationTimeBudget: evidenceInvalidBudget(),
    })
    const result = deriveAgentClaimPathTimeFit(cockpit, RUN_ID, 'CodexPlanning')
    expect(result).toEqual({ reason: 'EvidenceInvalid' })
    expect(isAgentClaimPathTimeFitBlocking(result)).toBe(true)
    expect(describeAgentClaimPathTimeFitBlock(result)).toMatch(/missing or invalid/i)
  })

  it('fails closed to ProjectionUnavailable when the cockpit is null', () => {
    const result = deriveAgentClaimPathTimeFit(null, RUN_ID, 'CodexPlanning')
    expect(result).toEqual({ reason: 'ProjectionUnavailable' })
    expect(isAgentClaimPathTimeFitBlocking(result)).toBe(true)
  })

  it('fails closed when the cockpit still describes a previously selected run, never for even one frame', () => {
    const staleCockpit = cockpitFor({ runId: 'run-0' } as Partial<GetRunCockpitResponse>)
    const result = deriveAgentClaimPathTimeFit(staleCockpit, RUN_ID, 'CodexPlanning')
    expect(result).toEqual({ reason: 'ProjectionUnavailable' })
  })

  it('fails closed when the per-claim-path list is missing entirely', () => {
    const cockpit = cockpitFor({ agentClaimPathTimeFits: undefined } as unknown as Partial<GetRunCockpitResponse>)
    const result = deriveAgentClaimPathTimeFit(cockpit, RUN_ID, 'CodexPlanning')
    expect(result).toEqual({ reason: 'ProjectionUnavailable' })
  })

  it('fails closed when the claim path is missing from the list', () => {
    const cockpit = cockpitFor({
      agentClaimPathTimeFits: sixFits().filter((entry) => entry.claimPath !== 'CodexPlanning'),
    })
    const result = deriveAgentClaimPathTimeFit(cockpit, RUN_ID, 'CodexPlanning')
    expect(result).toEqual({ reason: 'ProjectionUnavailable' })
  })

  it('fails closed when the claim path is duplicated in the list with agreeing values', () => {
    const cockpit = cockpitFor({ agentClaimPathTimeFits: [...sixFits(), fit('CodexPlanning', 'Fits')] })
    const result = deriveAgentClaimPathTimeFit(cockpit, RUN_ID, 'CodexPlanning')
    expect(result).toEqual({ reason: 'ProjectionUnavailable' })
  })

  it('fails closed and never resolves to Fits when the claim path is duplicated with contradictory values', () => {
    const cockpit = cockpitFor({
      agentClaimPathTimeFits: [...sixFits('CodexPlanning', 'Fits'), fit('CodexPlanning', 'DoesNotFit')],
    })
    const result = deriveAgentClaimPathTimeFit(cockpit, RUN_ID, 'CodexPlanning')
    expect(result).toEqual({ reason: 'ProjectionUnavailable' })
    expect(result.reason).not.toBe('Fits')
  })

  it('fails closed for an unrecognized fit string', () => {
    const cockpit = cockpitFor({ agentClaimPathTimeFits: sixFits('CodexPlanning', 'SomeFutureValue') })
    const result = deriveAgentClaimPathTimeFit(cockpit, RUN_ID, 'CodexPlanning')
    expect(result).toEqual({ reason: 'ProjectionUnavailable' })
  })

  it('fails closed for the whole projection when an unrecognized claim-path value appears anywhere in the list', () => {
    const cockpit = cockpitFor({ agentClaimPathTimeFits: [...sixFits(), fit('SomeFutureClaimPath', 'Fits')] })
    // Even a claim path unrelated to the malformed entry is not trusted, since the shape itself
    // is no longer one this backend can produce.
    const result = deriveAgentClaimPathTimeFit(cockpit, RUN_ID, 'CodeReview')
    expect(result).toEqual({ reason: 'ProjectionUnavailable' })
  })

  it('fails closed when the run-wide budget says isLegacyUnknown but this claim path reports Fits (contradictory projection)', () => {
    const cockpit = cockpitFor({
      agentClaimPathTimeFits: sixFits(),
      agentInvocationTimeBudget: legacyBudget(),
    })
    const result = deriveAgentClaimPathTimeFit(cockpit, RUN_ID, 'CodexPlanning')
    expect(result).toEqual({ reason: 'ProjectionUnavailable' })
  })

  it('fails closed when the run-wide budget says isLegacyUnknown but this claim path reports DoesNotFit (contradictory projection)', () => {
    const cockpit = cockpitFor({
      agentClaimPathTimeFits: sixFits('CodexPlanning', 'DoesNotFit'),
      agentInvocationTimeBudget: legacyBudget(),
    })
    const result = deriveAgentClaimPathTimeFit(cockpit, RUN_ID, 'CodexPlanning')
    expect(result).toEqual({ reason: 'ProjectionUnavailable' })
  })

  it('fails closed when this claim path reports LegacyUnknown but the run-wide budget is healthy (contradictory projection)', () => {
    const cockpit = cockpitFor({
      agentClaimPathTimeFits: sixFits('CodexPlanning', 'LegacyUnknown'),
      agentInvocationTimeBudget: healthyBudget(),
    })
    const result = deriveAgentClaimPathTimeFit(cockpit, RUN_ID, 'CodexPlanning')
    expect(result).toEqual({ reason: 'ProjectionUnavailable' })
  })

  it('fails closed when the run-wide budget says evidenceInvalid but this claim path reports Fits (contradictory projection)', () => {
    const cockpit = cockpitFor({
      agentClaimPathTimeFits: sixFits(),
      agentInvocationTimeBudget: evidenceInvalidBudget(),
    })
    const result = deriveAgentClaimPathTimeFit(cockpit, RUN_ID, 'CodexPlanning')
    expect(result).toEqual({ reason: 'ProjectionUnavailable' })
  })

  it('fails closed when this claim path reports EvidenceInvalid but the run-wide budget is healthy (contradictory projection)', () => {
    const cockpit = cockpitFor({
      agentClaimPathTimeFits: sixFits('CodexPlanning', 'EvidenceInvalid'),
      agentInvocationTimeBudget: healthyBudget(),
    })
    const result = deriveAgentClaimPathTimeFit(cockpit, RUN_ID, 'CodexPlanning')
    expect(result).toEqual({ reason: 'ProjectionUnavailable' })
  })

  it('fails closed when the run-wide invocation-time budget is missing entirely', () => {
    const cockpit = cockpitFor({ agentInvocationTimeBudget: undefined } as unknown as Partial<GetRunCockpitResponse>)
    const result = deriveAgentClaimPathTimeFit(cockpit, RUN_ID, 'CodexPlanning')
    expect(result).toEqual({ reason: 'ProjectionUnavailable' })
  })

  // --- All-six-entries coherence (this correction round) ---------------------------------------
  //
  // The requested path's own entry can look perfectly valid and coherent in isolation, but the
  // projection as a whole must still fail closed — for the REQUESTED path too — when any of the
  // OTHER five entries is unrecognized or contradicts the run-wide budget state. A malformed or
  // incoherent projection cannot be trusted for any path, including one that happens to look fine.

  it('fails closed for the requested path when a DIFFERENT path has an unrecognized fit value, even though the requested path itself reports Fits', () => {
    const cockpit = cockpitFor({
      agentClaimPathTimeFits: sixFits('ChallengeResolution', 'SomeFutureValue'),
    })
    const result = deriveAgentClaimPathTimeFit(cockpit, RUN_ID, 'CodexPlanning')
    expect(result).toEqual({ reason: 'ProjectionUnavailable' })
    expect(result.reason).not.toBe('Fits')
  })

  it('fails closed for the requested path when a DIFFERENT path reports LegacyUnknown while the run-wide budget is healthy, even though the requested path itself reports Fits', () => {
    const cockpit = cockpitFor({
      agentClaimPathTimeFits: sixFits('ReviewCorrection', 'LegacyUnknown'),
      agentInvocationTimeBudget: healthyBudget(),
    })
    const result = deriveAgentClaimPathTimeFit(cockpit, RUN_ID, 'CodexPlanning')
    expect(result).toEqual({ reason: 'ProjectionUnavailable' })
    expect(result.reason).not.toBe('Fits')
  })

  it('fails closed for the requested path when a DIFFERENT path reports Fits while the run-wide budget is legacy-unknown, even though the requested path itself correctly reports LegacyUnknown', () => {
    const cockpit = cockpitFor({
      agentClaimPathTimeFits: [
        ...CLAIM_PATHS.filter((path) => path !== 'Implementation').map((path) => fit(path, 'LegacyUnknown')),
        fit('Implementation', 'Fits'),
      ],
      agentInvocationTimeBudget: legacyBudget(),
    })
    const result = deriveAgentClaimPathTimeFit(cockpit, RUN_ID, 'CodexPlanning')
    expect(result).toEqual({ reason: 'ProjectionUnavailable' })
    expect(result.reason).not.toBe('LegacyUnknown')
  })

  it('fails closed for the requested path when a DIFFERENT path reports Fits while the run-wide budget has invalid evidence, even though the requested path itself correctly reports EvidenceInvalid', () => {
    const cockpit = cockpitFor({
      agentClaimPathTimeFits: [
        ...CLAIM_PATHS.filter((path) => path !== 'CodeReview').map((path) => fit(path, 'EvidenceInvalid')),
        fit('CodeReview', 'Fits'),
      ],
      agentInvocationTimeBudget: evidenceInvalidBudget(),
    })
    const result = deriveAgentClaimPathTimeFit(cockpit, RUN_ID, 'CodexPlanning')
    expect(result).toEqual({ reason: 'ProjectionUnavailable' })
    expect(result.reason).not.toBe('EvidenceInvalid')
  })

  it('happy path: a genuinely coherent six-entry set under a HEALTHY budget still resolves the requested path normally for every path, not just the requested one', () => {
    const cockpit = cockpitFor({
      agentClaimPathTimeFits: sixFits('Implementation', 'DoesNotFit'),
      agentInvocationTimeBudget: healthyBudget(),
    })
    expect(deriveAgentClaimPathTimeFit(cockpit, RUN_ID, 'CodexPlanning')).toEqual({ reason: 'Fits' })
    expect(deriveAgentClaimPathTimeFit(cockpit, RUN_ID, 'Implementation')).toEqual({ reason: 'DoesNotFit' })
  })

  it('happy path: a genuinely coherent six-entry set under a LEGACY-UNKNOWN budget (all six report LegacyUnknown) still resolves the requested path normally', () => {
    const cockpit = cockpitFor({
      agentClaimPathTimeFits: CLAIM_PATHS.map((path) => fit(path, 'LegacyUnknown')),
      agentInvocationTimeBudget: legacyBudget(),
    })
    const result = deriveAgentClaimPathTimeFit(cockpit, RUN_ID, 'CodexPlanning')
    expect(result).toEqual({ reason: 'LegacyUnknown' })
    expect(isAgentClaimPathTimeFitBlocking(result)).toBe(false)
  })

  it('happy path: a genuinely coherent six-entry set under an EVIDENCE-INVALID budget (all six report EvidenceInvalid) still resolves the requested path normally', () => {
    const cockpit = cockpitFor({
      agentClaimPathTimeFits: CLAIM_PATHS.map((path) => fit(path, 'EvidenceInvalid')),
      agentInvocationTimeBudget: evidenceInvalidBudget(),
    })
    const result = deriveAgentClaimPathTimeFit(cockpit, RUN_ID, 'CodexPlanning')
    expect(result).toEqual({ reason: 'EvidenceInvalid' })
    expect(isAgentClaimPathTimeFitBlocking(result)).toBe(true)
  })
})
