import { describe, expect, it } from 'vitest'
import { AgentAttemptHistoryResponse } from '../../api/generated/api-client'
import type { AgentAttemptHistoryEntryResponse } from '../../api/generated/api-client'
import { toAgentAttemptEvidence, toAgentAttemptHistoryPage, toAgentAttemptIdentityExpectation } from './agentAttemptHistory'
import type { AgentAttemptHistoryRequest, AgentAttemptIdentityExpectation } from './agentAttemptHistory'
import { attemptEntryFor, attemptEvidenceFor, attemptIdFor, attemptPageOf, descendingAttempts } from './agentAttemptHistoryFixture'

const RUN = 'run-7'
const first: AgentAttemptHistoryRequest = { runId: RUN, beforeAttemptNumber: null, limit: 10 }
const entry = (number: number, patch: Partial<AgentAttemptHistoryEntryResponse> = {}) => attemptEntryFor(RUN, number, patch)
const pageWith = (items: unknown[], hasMore = false, next?: number) =>
  new AgentAttemptHistoryResponse({ items: items as AgentAttemptHistoryEntryResponse[], hasMore, nextBeforeAttemptNumber: next })

describe('toAgentAttemptHistoryPage', () => {
  it('accepts a coherent last page and a coherent full continuation', () => {
    expect(toAgentAttemptHistoryPage(attemptPageOf(RUN, [3, 2, 1]), first)).toEqual({ entries: [entry(3), entry(2), entry(1)], nextBeforeAttemptNumber: null })

    const full = toAgentAttemptHistoryPage(attemptPageOf(RUN, descendingAttempts(45, 36), true), first)
    expect(full?.entries.map((row) => row.attemptNumber)).toEqual(descendingAttempts(45, 36))
    expect(full?.nextBeforeAttemptNumber).toBe(36)
  })

  it('accepts an empty first page as a successful empty history', () => {
    expect(toAgentAttemptHistoryPage(pageWith([]), first)).toEqual({ entries: [], nextBeforeAttemptNumber: null })
  })

  it('accepts an older page only below the cursor and the last retained row', () => {
    const retained = attemptPageOf(RUN, descendingAttempts(45, 36), true).items!
    const older: AgentAttemptHistoryRequest = { ...first, beforeAttemptNumber: 36 }

    expect(toAgentAttemptHistoryPage(attemptPageOf(RUN, [35, 34]), older, retained)?.entries).toHaveLength(2)
    expect(toAgentAttemptHistoryPage(attemptPageOf(RUN, [36, 35]), older, retained)).toBeNull()
    expect(toAgentAttemptHistoryPage(attemptPageOf(RUN, [40]), older, retained)).toBeNull()
    expect(toAgentAttemptHistoryPage(attemptPageOf(RUN, [37]), { ...first, beforeAttemptNumber: 38 }, retained)).toBeNull()
    // An older row that repeats a retained identity under another number is not new.
    expect(toAgentAttemptHistoryPage(pageWith([entry(30, { attemptId: attemptIdFor(RUN, 40) })]), older, retained)).toBeNull()
  })

  it.each<[string, unknown]>([
    ['a missing response', undefined],
    ['no hasMore verdict', new AgentAttemptHistoryResponse({ items: [] })],
    ['items that are not a list', { items: 'rows', hasMore: false }],
    ['more rows than requested', attemptPageOf(RUN, descendingAttempts(11, 1))],
    ['an ascending order', pageWith([entry(1), entry(2)])],
    ['a repeated number', pageWith([entry(2), entry(2, { attemptId: 'other' })])],
    ['a repeated identity', pageWith([entry(2), entry(1, { attemptId: attemptIdFor(RUN, 2) })])],
    ['a zero number', pageWith([entry(0)])],
    ['a fractional number', pageWith([entry(1.5)])],
    ['a blank identity', pageWith([entry(1, { attemptId: '  ' })])],
    ['no identity verdict', pageWith([entry(1, { identityValid: undefined })])],
    ['an invalid claim instant', pageWith([entry(1, { claimedAtUtc: new Date('not a date') })])],
    ['a claim instant that is not a Date', pageWith([{ ...entry(1), claimedAtUtc: '2026-10-09T10:01:00Z' }])],
    ['an invalid completion instant', pageWith([entry(1, { completedAtUtc: new Date('not a date') })])],
    ['a verified row without a status', pageWith([entry(1, { status: undefined })])],
    ['a verified row without a provider', pageWith([entry(1, { provider: '' })])],
    ['an unverified row that names its role', pageWith([entry(1, { identityValid: false, provider: undefined, responseContract: undefined, outcome: undefined })])],
    ['an unverified row that names its outcome', pageWith([entry(1, { identityValid: false, role: undefined, provider: undefined, responseContract: undefined })])],
    ['a repair number without an identity', pageWith([entry(3, { repairSourceAttemptNumber: 2 })])],
    ['a repair source that is not earlier', pageWith([entry(3, { repairSourceAttemptId: 'x', repairSourceAttemptNumber: 3 })])],
    ['a continuation that is not a full page', attemptPageOf(RUN, [4, 3], true)],
    ['a continuation without a cursor', pageWith(descendingAttempts(12, 3).map((n) => entry(n)), true)],
    ['a continuation cursor that is not the last number', pageWith(descendingAttempts(12, 3).map((n) => entry(n)), true, 4)],
    ['a cursor on a last page', pageWith([entry(2), entry(1)], false, 1)],
  ])('rejects the whole page for %s', (_label, response) => {
    expect(toAgentAttemptHistoryPage(response as AgentAttemptHistoryResponse, first)).toBeNull()
  })

  it.each([
    ['Planner', 'Codex', 'Proposal'],
    ['Resolver', 'Codex', 'ChallengeResolution'],
    ['CodeReviewer', 'Codex', 'ImplementationReview'],
    ['CodeReviewer', 'Codex', 'VerificationDiagnosis'],
    ['CriticalReviewer', 'ClaudeCode', 'CriticalReview'],
    ['Implementer', 'ClaudeCode', 'ImplementationReport'],
    ['Implementer', 'ClaudeCode', 'ReviewCorrection'],
  ])('accepts the supported verified identity %s · %s · %s', (role, provider, responseContract) => {
    expect(toAgentAttemptHistoryPage(pageWith([entry(1, { role, provider, responseContract })]), first)?.entries).toHaveLength(1)
  })

  it.each(['Running', 'Completed', 'Failed', 'Interrupted'])('accepts the recorded status %s on a verified row', (status) => {
    expect(toAgentAttemptHistoryPage(pageWith([entry(1, { status })]), first)?.entries).toHaveLength(1)
  })

  it.each<[string, Partial<AgentAttemptHistoryEntryResponse>]>([
    // HATT-R1: Codex's direct reproduction, every identity member unknown.
    ['an entirely unknown identity', { status: 'UnknownStatus', role: 'UnknownRole', provider: 'UnknownProvider', responseContract: 'UnknownContract' }],
    ['an unknown status', { status: 'UnknownStatus' }],
    ['a status of another letter case', { status: 'failed' }],
    ['an unknown role', { role: 'UnknownRole' }],
    ['a prototype-shaped role', { role: 'toString' }],
    ['an unknown provider', { provider: 'UnknownProvider' }],
    ['an unknown contract', { responseContract: 'UnknownContract' }],
    ['a prototype-shaped contract', { responseContract: 'constructor' }],
    ['a missing contract', { responseContract: undefined }],
    ['an unsupported pair (Planner · ClaudeCode)', { provider: 'ClaudeCode' }],
    ['an unsupported pair (Implementer · Codex)', { role: 'Implementer', provider: 'Codex', responseContract: 'ImplementationReport' }],
    ['a contract of another role (Planner · ImplementationReview)', { responseContract: 'ImplementationReview' }],
    ['a contract of another role (CriticalReviewer · ReviewCorrection)', { role: 'CriticalReviewer', provider: 'ClaudeCode', responseContract: 'ReviewCorrection' }],
  ])('rejects the whole page for a verified row with %s', (_label, patch) => {
    expect(toAgentAttemptHistoryPage(pageWith([entry(2), entry(1, patch)]), first)).toBeNull()
  })

  it('accepts an unverified row with only its number, times and readable or unreadable status', () => {
    const unverified = entry(2, { identityValid: false, role: undefined, provider: undefined, responseContract: undefined, outcome: undefined })
    const unreadable = entry(1, { identityValid: false, status: undefined, role: undefined, provider: undefined, responseContract: undefined, outcome: undefined })

    expect(toAgentAttemptHistoryPage(pageWith([unverified, unreadable]), first)?.entries).toEqual([unverified, unreadable])
  })

  it('accepts a proved repair source and one known by identity only', () => {
    const page = pageWith([entry(3, { repairSourceAttemptId: attemptIdFor(RUN, 2), repairSourceAttemptNumber: 2 }), entry(2, { repairSourceAttemptId: 'x' }), entry(1)])

    expect(toAgentAttemptHistoryPage(page, first)?.entries).toHaveLength(3)
  })
})

describe('toAgentAttemptEvidence', () => {
  const expected: AgentAttemptIdentityExpectation = {
    attemptId: attemptIdFor(RUN, 4),
    attemptNumber: 4,
    role: 'Planner',
    provider: 'Codex',
    responseContract: 'Proposal',
  }

  it('accepts evidence for exactly the selected attempt', () => {
    const evidence = attemptEvidenceFor(RUN, 4, ['AgentContextManifest', 'AgentStandardError'])

    expect(toAgentAttemptEvidence(evidence, expected)).toEqual({ status: 'success', evidence })
  })

  it('reports an unverified attempt of exactly the selected identity as identity-invalid, whatever else it carries', () => {
    const evidence = attemptEvidenceFor(RUN, 4, ['toString'], { identityValid: false, role: undefined, provider: undefined })

    expect(toAgentAttemptEvidence(evidence, expected)).toEqual({ status: 'identity-invalid' })
  })

  it.each<[string, unknown]>([
    ['a missing answer', null],
    ['another identity', attemptEvidenceFor(RUN, 4, [], { attemptId: attemptIdFor(RUN, 5) })],
    ['an identity of another letter case', attemptEvidenceFor(RUN, 4, [], { attemptId: attemptIdFor(RUN, 4).toUpperCase() })],
    ['another number', attemptEvidenceFor(RUN, 4, [], { attemptNumber: 5 })],
    ['an unverified answer for another attempt', attemptEvidenceFor(RUN, 5, [], { identityValid: false })],
    ['no identity verdict', attemptEvidenceFor(RUN, 4, [], { identityValid: undefined })],
    ['no status', attemptEvidenceFor(RUN, 4, [], { attemptStatus: undefined })],
    ['no role', attemptEvidenceFor(RUN, 4, [], { role: ' ' })],
    ['an invalid claim instant', attemptEvidenceFor(RUN, 4, [], { claimedAtUtc: new Date('x') })],
    ['no artifact list', attemptEvidenceFor(RUN, 4, [], { artifacts: undefined })],
    ['a Process purpose', attemptEvidenceFor(RUN, 4, ['ProcessStandardOutput'])],
    ['a prototype-shaped purpose', attemptEvidenceFor(RUN, 4, ['constructor'])],
    ['a repeated purpose', attemptEvidenceFor(RUN, 4, ['AgentFinalResponse', 'AgentFinalResponse'])],
  ])('fails closed on %s', (_label, response) => {
    expect(toAgentAttemptEvidence(response as never, expected)).toBeNull()
  })

  it('accepts a status that advanced after the page was read, because status and times are fresh observations', () => {
    const advanced = attemptEvidenceFor(RUN, 4, ['AgentFinalResponse'], {
      attemptStatus: 'Completed',
      outcome: 'Proposed',
      completedAtUtc: new Date(Date.UTC(2026, 9, 9, 11, 0, 0)),
    })
    const selectedWhileRunning = toAgentAttemptIdentityExpectation(attemptEntryFor(RUN, 4, { status: 'Running', outcome: undefined }))

    expect(selectedWhileRunning).toEqual(expected)
    expect(toAgentAttemptEvidence(advanced, selectedWhileRunning!)).toEqual({ status: 'success', evidence: advanced })
  })

  it.each<[string, Record<string, unknown>]>([
    // HATT-R1: Codex's direct reproduction, every identity member unknown.
    ['an entirely unknown identity', { attemptStatus: 'UnknownStatus', role: 'UnknownRole', provider: 'UnknownProvider', responseContract: 'UnknownContract' }],
    ['an unknown status', { attemptStatus: 'UnknownStatus' }],
    ['an unknown role', { role: 'UnknownRole' }],
    ['an unknown provider', { provider: 'UnknownProvider' }],
    ['an unknown contract', { responseContract: 'UnknownContract' }],
    ['a missing contract', { responseContract: undefined }],
    ['an unsupported pair', { provider: 'ClaudeCode' }],
    ['a contract of another role', { responseContract: 'ImplementationReview' }],
    // Recognized and coherent identities that contradict the selected Planner · Codex · Proposal row.
    ['another supported role, provider and contract', { role: 'Resolver', responseContract: 'ChallengeResolution' }],
    ['another provider with its own role and contract', { role: 'Implementer', provider: 'ClaudeCode', responseContract: 'ImplementationReport' }],
    ['another contract of a role sharing the provider', { role: 'CodeReviewer', responseContract: 'VerificationDiagnosis' }],
  ])('fails closed on verified evidence with %s, exposing none of its metadata', (_label, patch) => {
    expect(toAgentAttemptEvidence(attemptEvidenceFor(RUN, 4, ['AgentStandardOutput'], patch as never), expected)).toBeNull()
  })

  it('binds evidence to each part of the selected identity, not only its attempt and number', () => {
    const implementer = attemptEvidenceFor(RUN, 4, [], { role: 'Implementer', provider: 'ClaudeCode', responseContract: 'ReviewCorrection' })

    expect(toAgentAttemptEvidence(implementer, { ...expected, role: 'Implementer', provider: 'ClaudeCode', responseContract: 'ReviewCorrection' })?.status).toBe('success')
    expect(toAgentAttemptEvidence(implementer, { ...expected, role: 'Implementer', provider: 'ClaudeCode', responseContract: 'ImplementationReport' })).toBeNull()
  })

  it('derives an expectation only from a verified row with a coherent identity', () => {
    expect(toAgentAttemptIdentityExpectation(attemptEntryFor(RUN, 4))).toEqual(expected)
    expect(toAgentAttemptIdentityExpectation(attemptEntryFor(RUN, 4, { identityValid: false }))).toBeNull()
    expect(toAgentAttemptIdentityExpectation(attemptEntryFor(RUN, 4, { responseContract: 'CriticalReview' }))).toBeNull()
  })

  it('fails closed on artifact metadata with an impossible length or truncation flag', () => {
    const base = attemptEvidenceFor(RUN, 4, ['AgentStandardOutput'])
    const withArtifact = (artifact: Record<string, unknown>) => ({ ...base, artifacts: [artifact] }) as never

    expect(toAgentAttemptEvidence(withArtifact({ purpose: 'AgentStandardOutput', byteLength: -1 }), expected)).toBeNull()
    expect(toAgentAttemptEvidence(withArtifact({ purpose: 'AgentStandardOutput', byteLength: 1.5 }), expected)).toBeNull()
    expect(toAgentAttemptEvidence(withArtifact({ purpose: 'AgentStandardOutput', byteLength: 3, truncated: 'yes' }), expected)).toBeNull()
    expect(toAgentAttemptEvidence(withArtifact({ purpose: 'AgentStandardOutput', byteLength: 0 }), expected)?.status).toBe('success')
  })
})
