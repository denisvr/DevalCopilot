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
