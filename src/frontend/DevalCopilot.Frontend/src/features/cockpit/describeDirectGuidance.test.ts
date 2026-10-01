import { describe, expect, it } from 'vitest'
import { DirectHumanGuidanceResponse } from '../../api/generated/api-client'
import { checkDirectGuidanceDraft, describeDirectGuidance, directGuidanceLength } from './describeDirectGuidance'

const fact = (state: string, text?: string) => new DirectHumanGuidanceResponse({ state, text })

describe('describeDirectGuidance', () => {
  it('has no fact for an absent response', () => {
    expect(describeDirectGuidance(null)).toBeNull()
    expect(describeDirectGuidance(undefined)).toBeNull()
  })

  it('maps the three states, and a legacy NotProvided state from an older server is unknown', () => {
    expect(describeDirectGuidance(fact('Provided', 'Keep it small.'))).toEqual({ kind: 'provided', text: 'Keep it small.' })
    expect(describeDirectGuidance(fact('NotProvided'))).toEqual({ kind: 'unknown' })
    expect(describeDirectGuidance(fact('NotRecorded'))).toEqual({ kind: 'notRecorded' })
    expect(describeDirectGuidance(fact('Unknown', 'must not appear'))).toEqual({ kind: 'unknown' })
  })

  it.each([
    ['Provided without text', fact('Provided')],
    ['Provided with blank text', fact('Provided', '  \n ')],
    ['Provided with over-long text', fact('Provided', 'x'.repeat(601))],
    ['an unrecognised state', fact('Surprise', 'text')],
    ['a missing state', new DirectHumanGuidanceResponse({ text: 'text' })],
  ])('treats %s as unknown and never carries text', (_name, input) => {
    expect(describeDirectGuidance(input)).toEqual({ kind: 'unknown' })
  })
})

describe('checkDirectGuidanceDraft', () => {
  it('rejects blank and whitespace-only drafts', () => {
    expect(checkDirectGuidanceDraft('')).toEqual({ ok: false, reason: 'blank' })
    expect(checkDirectGuidanceDraft(' \r\n\t ')).toEqual({ ok: false, reason: 'blank' })
  })

  it('counts UTF-16 code units of the normalized, trimmed text', () => {
    expect(checkDirectGuidanceDraft(`  ${'x'.repeat(600)}  `)).toEqual({ ok: true })
    expect(checkDirectGuidanceDraft('x'.repeat(601))).toEqual({ ok: false, reason: 'tooLong' })
    // Each emoji is two UTF-16 code units: 300 fit, 301 do not.
    expect(checkDirectGuidanceDraft('😀'.repeat(300))).toEqual({ ok: true })
    expect(checkDirectGuidanceDraft('😀'.repeat(301))).toEqual({ ok: false, reason: 'tooLong' })
    // CRLF counts as one newline, as the server normalizes it.
    expect(directGuidanceLength('a\r\nb')).toBe(3)
    // Normalization form C composes e + combining acute into one unit.
    expect(directGuidanceLength('é')).toBe(1)
  })

  it('allows a newline but no other control character', () => {
    expect(checkDirectGuidanceDraft('line one\nline two')).toEqual({ ok: true })
    expect(checkDirectGuidanceDraft('line one\r\nline two')).toEqual({ ok: true })
    expect(checkDirectGuidanceDraft('tab\there')).toEqual({ ok: false, reason: 'control' })
    expect(checkDirectGuidanceDraft('nul\u0000here')).toEqual({ ok: false, reason: 'control' })
    expect(checkDirectGuidanceDraft('del\u007fhere')).toEqual({ ok: false, reason: 'control' })
    expect(checkDirectGuidanceDraft('c1\u0085here')).toEqual({ ok: false, reason: 'control' })
  })
})
