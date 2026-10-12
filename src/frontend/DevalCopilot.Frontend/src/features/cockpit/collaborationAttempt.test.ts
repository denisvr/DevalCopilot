import { describe, expect, it } from 'vitest'
import { toCollaborationAttempt } from './collaborationAttempt'
import type { CollaborationAttemptSource } from './collaborationAttempt'
import { attemptAnswerFor, attemptHeaderFor } from './collaborationHistoryFixture'

const source = (overrides: Partial<CollaborationAttemptSource> = {}): CollaborationAttemptSource => ({
  runId: 'run-1',
  messageId: 'message-20',
  sequence: '20',
  type: 'Proposal',
  protocolVersion: '1.0',
  provenance: 'ProviderObserved',
  actor: { kind: 'Agent', role: 'Planner', provider: 'Codex' },
  ...overrides,
})

const answer = (observed: CollaborationAttemptSource, status: string, header?: ReturnType<typeof attemptHeaderFor>) =>
  attemptAnswerFor({ id: observed.messageId, sequence: observed.sequence, runId: observed.runId }, status, header)

const SUPPORTED = [
  ['Planner', 'Codex', 'Proposal'],
  ['CriticalReviewer', 'ClaudeCode', 'CriticalReview'],
  ['Resolver', 'Codex', 'ChallengeResolution'],
  ['Implementer', 'ClaudeCode', 'ImplementationReport'],
  ['Implementer', 'ClaudeCode', 'ReviewCorrection'],
  ['CodeReviewer', 'Codex', 'ImplementationReview'],
  ['CodeReviewer', 'Codex', 'VerificationDiagnosis'],
] as const

const ROLES = ['Planner', 'CriticalReviewer', 'Resolver', 'Implementer', 'CodeReviewer'] as const
const PROVIDERS = ['Codex', 'ClaudeCode'] as const
const CONTRACTS = [
  'Proposal',
  'CriticalReview',
  'ChallengeResolution',
  'ImplementationReport',
  'ImplementationReview',
  'ReviewCorrection',
  'VerificationDiagnosis',
] as const

describe('toCollaborationAttempt', () => {
  it('accepts an Available header for a coherent provider-observed source and keeps every recorded fact', () => {
    const observed = source()

    const result = toCollaborationAttempt(answer(observed, 'Available', attemptHeaderFor()), observed)

    expect(result).toEqual({
      status: 'Available',
      attempt: {
        id: 'attempt-7',
        attemptNumber: 3,
        role: 'Planner',
        provider: 'Codex',
        responseContract: 'Proposal',
        status: 'Completed',
        claimedAtUtc: new Date(Date.UTC(2026, 9, 12, 9, 0, 0)),
        dispatchedAtUtc: new Date(Date.UTC(2026, 9, 12, 9, 1, 2)),
        completedAtUtc: new Date(Date.UTC(2026, 9, 12, 9, 7, 8)),
      },
    })
  })

  it.each(SUPPORTED)('accepts the supported %s on %s with the %s contract when the actor matches', (role, provider, contract) => {
    const observed = source({ actor: { kind: 'Agent', role, provider } })

    const result = toCollaborationAttempt(answer(observed, 'Available', attemptHeaderFor({ role, provider, responseContract: contract })), observed)

    expect(result?.status).toBe('Available')
  })

  it.each(ROLES.flatMap((role) => PROVIDERS.flatMap((provider) => CONTRACTS.map((contract) => [role, provider, contract] as const))))(
    'for a %s on %s under %s agrees with the supported pairs and the contract-to-role table',
    (role, provider, contract) => {
      const observed = source({ actor: { kind: 'Agent', role, provider } })

      const result = toCollaborationAttempt(answer(observed, 'Available', attemptHeaderFor({ role, provider, responseContract: contract })), observed)

      expect(result !== null).toBe(SUPPORTED.some(([r, p, c]) => r === role && p === provider && c === contract))
    },
  )

  it('refuses a header whose role or provider is not the source actor', () => {
    const observed = source()

    expect(toCollaborationAttempt(answer(observed, 'Available', attemptHeaderFor({ role: 'Resolver', responseContract: 'ChallengeResolution' })), observed)).toBeNull()
    expect(
      toCollaborationAttempt(
        answer(observed, 'Available', attemptHeaderFor({ role: 'Implementer', provider: 'ClaudeCode', responseContract: 'ImplementationReport' })),
        observed,
      ),
    ).toBeNull()
    for (const actor of [
      { kind: 'Human', role: null, provider: null },
      { kind: 'Orchestrator', role: null, provider: null },
      { kind: 'Unrecognized', role: null, provider: null },
      { kind: 'Agent', role: null, provider: 'Codex' },
    ] as const) {
      const other = source({ actor })
      expect(toCollaborationAttempt(answer(other, 'Available', attemptHeaderFor()), other)).toBeNull()
    }
  })

  it('accepts NoAgentAttempt only for a recognized, non-provider-observed source and never with an attempt', () => {
    for (const provenance of ['Simulated', 'HostConstructed', 'HumanSubmitted'] as const) {
      const observed = source({ provenance, actor: { kind: 'Human', role: null, provider: null } })
      expect(toCollaborationAttempt(answer(observed, 'NoAgentAttempt'), observed)).toEqual({ status: 'NoAgentAttempt' })
      expect(toCollaborationAttempt(answer(observed, 'NoAgentAttempt', attemptHeaderFor()), observed)).toBeNull()
    }

    for (const observed of [
      source(),
      source({ provenance: 'Unrecognized' }),
      source({ type: 'Unrecognized', provenance: 'Simulated' }),
      source({ protocolVersion: 'Unrecognized', provenance: 'Simulated' }),
    ]) {
      expect(toCollaborationAttempt(answer(observed, 'NoAgentAttempt'), observed)).toBeNull()
    }
  })

  it('accepts Unavailable for every source and refuses a header beside it', () => {
    for (const observed of [source(), source({ provenance: 'Unrecognized' }), source({ type: 'Unrecognized' }), source({ protocolVersion: 'Unrecognized' })]) {
      expect(toCollaborationAttempt(answer(observed, 'Unavailable'), observed)).toEqual({ status: 'Unavailable' })
      expect(toCollaborationAttempt(answer(observed, 'Unavailable', attemptHeaderFor()), observed)).toBeNull()
    }
  })

  it('refuses an Available answer without a header, or for a source that is not recognized and provider-observed', () => {
    const observed = source()

    expect(toCollaborationAttempt(answer(observed, 'Available'), observed)).toBeNull()
    for (const other of [
      source({ provenance: 'Simulated' }),
      source({ provenance: 'Unrecognized' }),
      source({ type: 'Unrecognized' }),
      source({ protocolVersion: 'Unrecognized' }),
    ]) {
      expect(toCollaborationAttempt(answer(other, 'Available', attemptHeaderFor()), other)).toBeNull()
    }
  })

  it.each([
    ['another run', { runId: 'run-2' }],
    ['another message', { messageId: 'message-21' }],
    ['another sequence', { sourceSequence: '21' }],
    ['a padded sequence', { sourceSequence: '020' }],
    ['no sequence', { sourceSequence: undefined }],
    ['no status', { status: undefined }],
    ['an unknown status', { status: 'Pending' }],
    ['a lowercase status', { status: 'available' }],
    ['the unrecognized status', { status: 'Unrecognized' }],
  ])('refuses an answer that names %s', (_name, patch) => {
    const observed = source()
    const response = answer(observed, 'Available', attemptHeaderFor())
    Object.assign(response, patch)

    expect(toCollaborationAttempt(response, observed)).toBeNull()
  })

  it('refuses a missing response and an observed source sequence that is not canonical', () => {
    expect(toCollaborationAttempt(null, source())).toBeNull()
    expect(toCollaborationAttempt(undefined, source())).toBeNull()
    for (const sequence of ['0', '-1', '007', '9223372036854775808', '1e3', '']) {
      const observed = source({ sequence })
      expect(toCollaborationAttempt(answer(observed, 'Available', attemptHeaderFor()), observed)).toBeNull()
    }
  })

  it('accepts a source sequence up to the Int64 maximum exactly', () => {
    for (const sequence of ['9007199254740993', '9223372036854775807']) {
      const observed = source({ sequence })
      expect(toCollaborationAttempt(answer(observed, 'Unavailable'), observed)).toEqual({ status: 'Unavailable' })
    }
  })

  it.each([
    ['no identity', { id: undefined }],
    ['a blank identity', { id: '   ' }],
    ['number zero', { attemptNumber: 0 }],
    ['a negative number', { attemptNumber: -2 }],
    ['a fractional number', { attemptNumber: 1.5 }],
    ['an overflowing number', { attemptNumber: 2_147_483_648 }],
    ['a non-numeric number', { attemptNumber: '3' as unknown as number }],
    ['an unknown role', { role: 'Overlord' }],
    ['an unknown provider', { provider: 'Gemini' }],
    ['an unknown contract', { responseContract: 'Nonsense' }],
    ['no contract', { responseContract: undefined }],
    ['an unknown status', { status: 'Exploded' }],
    ['no claim time', { claimedAtUtc: undefined }],
    ['an invalid claim time', { claimedAtUtc: new Date(Number.NaN) }],
    ['an invalid dispatch time', { dispatchedAtUtc: new Date(Number.NaN) }],
    ['an invalid completion time', { completedAtUtc: new Date(Number.NaN) }],
    ['a text time', { claimedAtUtc: '2026-10-12T09:00:00Z' as unknown as Date }],
  ])('refuses a header with %s', (_name, patch) => {
    const observed = source()

    expect(toCollaborationAttempt(answer(observed, 'Available', attemptHeaderFor(patch)), observed)).toBeNull()
  })

  it('keeps absent dispatch and completion times as null and rejects nothing about their order', () => {
    const observed = source()

    const result = toCollaborationAttempt(
      answer(observed, 'Available', attemptHeaderFor({ status: 'Running', dispatchedAtUtc: undefined, completedAtUtc: undefined })),
      observed,
    )

    expect(result).toMatchObject({ status: 'Available', attempt: { status: 'Running', dispatchedAtUtc: null, completedAtUtc: null } })
  })

  it('accepts every recorded status', () => {
    const observed = source()
    for (const status of ['Running', 'Completed', 'Failed', 'Interrupted']) {
      expect(toCollaborationAttempt(answer(observed, 'Available', attemptHeaderFor({ status })), observed)).toMatchObject({
        status: 'Available',
        attempt: { status },
      })
    }
  })
})
