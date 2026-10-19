import { describe, expect, it } from 'vitest'
import { parseEventSummary } from './parseEventSummary'

describe('parseEventSummary', () => {
  it('prefers a summary, then an objective, then the raw payload', () => {
    expect(parseEventSummary('{"summary":"A bounded summary","objective":"An objective"}')).toBe('A bounded summary')
    expect(parseEventSummary('{"objective":"An objective"}')).toBe('An objective')
    expect(parseEventSummary('{"other":1}')).toBe('{"other":1}')
    expect(parseEventSummary('not json')).toBe('not json')
  })

  it('words the abandonment event from its type and its reason, with the reason kept as plain text', () => {
    const payload = JSON.stringify({ reason: 'No longer needed\nSecond line <b>x</b>' })

    expect(parseEventSummary(payload, 'run.abandoned')).toBe('Run abandoned by the owner. Reason: No longer needed\nSecond line <b>x</b>')
  })

  it('never takes a reason from the payload of any other event type', () => {
    const payload = JSON.stringify({ reason: 'local_commit.git_unprovable' })

    expect(parseEventSummary(payload, 'local_commit.failed')).toBe(payload)
    expect(parseEventSummary(payload)).toBe(payload)
  })

  it('does not invent a reason for an abandonment event whose payload has none or a non-text one', () => {
    expect(parseEventSummary('{"reason":42}', 'run.abandoned')).toBe('{"reason":42}')
    expect(parseEventSummary('{}', 'run.abandoned')).toBe('{}')
    expect(parseEventSummary('garbage', 'run.abandoned')).toBe('garbage')
  })
})

describe('parseEventSummary for the GitHub observation target event', () => {
  const payload = JSON.stringify({
    version: 1,
    targetId: 't',
    operationId: 'o',
    commitSha: 'a'.repeat(40),
    branchName: 'devalcopilot/workspace/x/1',
    host: 'github.com',
    owner: 'octo-cat',
    repository: 'hello.world',
  })

  it('words it from its type and its own text members as a local selection only', () => {
    expect(parseEventSummary(payload, 'run.github_observation_target_recorded')).toBe(
      `GitHub observation target selected by the owner (local selection only): github.com/octo-cat/hello.world for local commit ${'a'.repeat(40)}.`,
    )
  })

  it('never words another event type, or an incomplete or non-text payload, as a selection', () => {
    expect(parseEventSummary(payload, 'run.completed')).toBe(payload)
    expect(parseEventSummary('{"owner":"x","repository":"y"}', 'run.github_observation_target_recorded')).toBe('{"owner":"x","repository":"y"}')
    expect(parseEventSummary('{"owner":1,"repository":"y","commitSha":"z"}', 'run.github_observation_target_recorded')).toBe(
      '{"owner":1,"repository":"y","commitSha":"z"}',
    )
    expect(parseEventSummary('garbage', 'run.github_observation_target_recorded')).toBe('garbage')
  })
})

describe('the GitHub repository identity bound event', () => {
  const payload = JSON.stringify({
    version: 1,
    bindingId: 'b',
    targetId: 't',
    operationId: 'o',
    commitSha: 'a'.repeat(40),
    branchName: 'devalcopilot/workspace/x/1',
    host: 'github.com',
    repositoryNodeId: 'R_kgDOExample01',
    owner: 'octo-cat',
    repository: 'hello.world',
    observedAtUtc: '2026-10-13T12:00:00+00:00',
  })

  it('words it from its type and its own text members as historical identity evidence only', () => {
    expect(parseEventSummary(payload, 'run.github_repository_identity_bound')).toBe(
      `GitHub repository identity bound by the owner (historical identity evidence only): github.com/octo-cat/hello.world with repository ID R_kgDOExample01 for local commit ${'a'.repeat(40)}.`,
    )
  })

  it('never words another type, or an incomplete or non-text payload, as a binding', () => {
    expect(parseEventSummary(payload, 'run.completed')).toBe(payload)
    expect(parseEventSummary('{"owner":"x","repository":"y","commitSha":"z"}', 'run.github_repository_identity_bound')).toBe(
      '{"owner":"x","repository":"y","commitSha":"z"}',
    )
    expect(parseEventSummary('{"owner":"x","repository":"y","commitSha":"z","repositoryNodeId":1}', 'run.github_repository_identity_bound')).toBe(
      '{"owner":"x","repository":"y","commitSha":"z","repositoryNodeId":1}',
    )
    expect(parseEventSummary('garbage', 'run.github_repository_identity_bound')).toBe('garbage')
  })
})

describe('the GitHub branch publication approved event', () => {
  const payload = JSON.stringify({
    version: 1,
    approvalId: 'a',
    runId: 'r',
    projectId: 'p',
    operationId: 'o',
    workspaceId: 'w',
    commitSha: 'a'.repeat(40),
    branchName: 'devalcopilot/workspace/x/1',
    host: 'github.com',
    action: 'PushExactCommit',
    policyVersion: 1,
    target: { id: 't', eventId: 'e', owner: 'octo-cat', repository: 'hello.world', recordedAtUtc: '2026-10-14T12:00:00+00:00' },
    binding: { id: 'b', eventId: 'be', repositoryNodeId: 'R_kgDOExample01', owner: 'Octo-Cat', repository: 'Hello.World' },
    approvedAtUtc: '2026-10-14T12:03:00+00:00',
  })

  it('words it as recorded consent for one future push and says nothing was pushed', () => {
    const summary = parseEventSummary(payload, 'run.github_branch_publication_approved')

    expect(summary).toBe(
      `GitHub branch publication approved by the owner (recorded consent for one future push only; nothing was pushed): github.com/octo-cat/hello.world branch devalcopilot/workspace/x/1 at local commit ${'a'.repeat(40)}.`,
    )
    expect(summary).not.toMatch(/published|pushed to|force|merge|pull request/i)
  })

  it('never words another type, or an incomplete or non-text payload, as an approval', () => {
    expect(parseEventSummary(payload, 'run.completed')).toBe(payload)
    expect(parseEventSummary(payload)).toBe(payload)
    const incomplete = '{"commitSha":"z","branchName":"b","target":{"owner":"x"}}'
    expect(parseEventSummary(incomplete, 'run.github_branch_publication_approved')).toBe(incomplete)
    const wrongType = '{"commitSha":"z","branchName":"b","target":{"owner":1,"repository":"y"}}'
    expect(parseEventSummary(wrongType, 'run.github_branch_publication_approved')).toBe(wrongType)
    const noTarget = '{"commitSha":"z","branchName":"b"}'
    expect(parseEventSummary(noTarget, 'run.github_branch_publication_approved')).toBe(noTarget)
    expect(parseEventSummary('garbage', 'run.github_branch_publication_approved')).toBe('garbage')
  })

  describe('planning sequence events', () => {
    const types = [
      'planning_sequence.armed',
      'planning_sequence.step_reserved',
      'planning_sequence.step_claimed',
      'planning_sequence.step_concluded',
      'planning_sequence.stop_requested',
    ]

    it.each(types)('words %s from its fixed type only, never from the payload', (type) => {
      const summary = parseEventSummary('{"summary":"payload text that must not appear","sequenceId":"x"}', type)

      expect(summary).not.toContain('payload text')
      expect(summary.length).toBeGreaterThan(10)
      expect(parseEventSummary('not json {', type)).toBe(summary)
      expect(parseEventSummary('null', type)).toBe(summary)
    })

    it('never words a claim as a running process', () => {
      expect(parseEventSummary('{}', 'planning_sequence.step_claimed')).toMatch(/not proof that a provider process is running/)
    })

    it('words the end from its closed state and reason, and as the plain sentence for anything else', () => {
      expect(parseEventSummary('{"state":"Stopped","reason":"stopped_before_claim","detail":null}', 'planning_sequence.ended')).toBe(
        'The planning sequence ended: stopped; stopped before the next step was claimed.',
      )
      const plain = 'The planning sequence ended.'
      expect(parseEventSummary('{"state":"Exploded","reason":"stopped_before_claim"}', 'planning_sequence.ended')).toBe(plain)
      expect(parseEventSummary('{"state":"Stopped","reason":"<b>x</b>"}', 'planning_sequence.ended')).toBe(plain)
      expect(parseEventSummary('{"summary":"payload"}', 'planning_sequence.ended')).toBe(plain)
    })

    it('leaves an unknown planning-sequence type to the ordinary payload rule', () => {
      expect(parseEventSummary('{"summary":"x"}', 'planning_sequence.unknown')).toBe('x')
    })
  })
})
