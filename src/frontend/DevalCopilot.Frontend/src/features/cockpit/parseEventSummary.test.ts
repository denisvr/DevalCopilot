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
