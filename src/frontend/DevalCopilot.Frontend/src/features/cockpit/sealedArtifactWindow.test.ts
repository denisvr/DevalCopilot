import { describe, expect, it } from 'vitest'
import { SealedAgentArtifactWindowResponse } from '../../api/generated/api-client'
import { decideSealedWindowAnswer } from './sealedArtifactWindow'
import type { SealedWindowRequestFacts } from './sealedArtifactWindow'

const first: SealedWindowRequestFacts = { fromOffset: 0, maxBytes: 16, sealedTotal: null }
const later: SealedWindowRequestFacts = { fromOffset: 16, maxBytes: 16, sealedTotal: 40 }

// The raw wire shape, as the generated client's fromJS copies it without checking (absent members stay absent).
const wire = (fields: Record<string, unknown>) => SealedAgentArtifactWindowResponse.fromJS(fields)
const ok = (fields: Record<string, unknown>) => wire({ status: 'Ok', truncated: false, ...fields })

describe('decideSealedWindowAnswer', () => {
  it('accepts a coherent first window and reports its cursor, sealed length and truncation', () => {
    expect(decideSealedWindowAnswer(ok({ text: 'abcd', nextOffset: 4, totalLengthSoFar: 10, truncated: null }), first)).toEqual({
      kind: 'ok',
      text: 'abcd',
      nextOffset: 4,
      totalLength: 10,
      truncated: null,
    })
  })

  it('accepts a later window that reaches the end of the sealed file with the same sealed length', () => {
    expect(decideSealedWindowAnswer(ok({ text: 'x'.repeat(16), nextOffset: 32, totalLengthSoFar: 40, truncated: true }), later)).toMatchObject({
      kind: 'ok',
      nextOffset: 32,
      truncated: true,
    })
    expect(decideSealedWindowAnswer(ok({ text: 'tail', nextOffset: 40, totalLengthSoFar: 40 }), { ...later, fromOffset: 36 })).toMatchObject({
      kind: 'ok',
      nextOffset: 40,
    })
  })

  it('accepts a legitimate empty sealed file and a read at the end of the file, which advance by zero', () => {
    expect(decideSealedWindowAnswer(ok({ text: '', nextOffset: 0, totalLengthSoFar: 0 }), first)).toMatchObject({ kind: 'ok', totalLength: 0 })
    expect(decideSealedWindowAnswer(ok({ text: '', nextOffset: 40, totalLengthSoFar: 40 }), { ...later, fromOffset: 40 })).toMatchObject({
      kind: 'ok',
      nextOffset: 40,
    })
  })

  // UTF-8 cursor semantics of the host: byte offsets, a split codepoint deferred whole to the next window, and malformed bytes decoded
  // with replacement. None of these makes the advance equal to the text's UTF-16 length or to its re-encoded byte count.
  it.each([
    ['multi-byte characters (5 bytes, 2 code units)', '\u00E9\u20AC', 5],
    ['a four-byte codepoint (4 bytes, 2 code units)', '\u{1F600}', 4],
    ['a window cut before a deferred split codepoint (3 bytes)', 'abc', 3],
    ['malformed bytes decoded with one replacement each (2 bytes)', '\uFFFD\uFFFD', 2],
    ['a truncated sequence at the end decoded as one replacement (3 bytes)', '\uFFFD', 3],
  ])('accepts %s without recomputing the advance from the text', (_name, text, advance) => {
    expect(decideSealedWindowAnswer(ok({ text, nextOffset: advance, totalLengthSoFar: 10 }), first)).toMatchObject({
      kind: 'ok',
      text,
      nextOffset: advance,
    })
  })

  it.each([
    ['an absent text', { nextOffset: 4, totalLengthSoFar: 10 }],
    ['a null text', { text: null, nextOffset: 4, totalLengthSoFar: 10 }],
    ['a numeric text', { text: 4, nextOffset: 4, totalLengthSoFar: 10 }],
    ['an absent truncation', { text: 'abcd', nextOffset: 4, totalLengthSoFar: 10, truncated: undefined }],
    ['a string truncation', { text: 'abcd', nextOffset: 4, totalLengthSoFar: 10, truncated: 'false' }],
    ['an absent next offset', { text: 'abcd', totalLengthSoFar: 10 }],
    ['an absent total', { text: 'abcd', nextOffset: 4 }],
    ['a null next offset', { text: 'abcd', nextOffset: null, totalLengthSoFar: 10 }],
    ['a string next offset', { text: 'abcd', nextOffset: '4', totalLengthSoFar: 10 }],
    ['a negative total', { text: '', nextOffset: 0, totalLengthSoFar: -1 }],
    ['a fractional next offset', { text: 'abcd', nextOffset: 4.5, totalLengthSoFar: 10 }],
    ['a fractional total', { text: 'abcd', nextOffset: 4, totalLengthSoFar: 10.5 }],
    ['an unsafe total', { text: 'abcd', nextOffset: 4, totalLengthSoFar: Number.MAX_SAFE_INTEGER + 1 }],
    ['an infinite total', { text: 'abcd', nextOffset: 4, totalLengthSoFar: Infinity }],
    ['a NaN next offset', { text: 'abcd', nextOffset: NaN, totalLengthSoFar: 10 }],
    ['a next offset beyond the total', { text: 'abcd', nextOffset: 11, totalLengthSoFar: 10 }],
    ['an advance beyond the requested byte cap', { text: 'abcd', nextOffset: 17, totalLengthSoFar: 20 }],
    ['no progress while bytes remain', { text: '', nextOffset: 0, totalLengthSoFar: 10 }],
    ['text without progress', { text: 'abcd', nextOffset: 0, totalLengthSoFar: 0 }],
    ['progress without text', { text: '', nextOffset: 4, totalLengthSoFar: 10 }],
    ['more UTF-16 code units than bytes consumed', { text: 'abcde', nextOffset: 4, totalLengthSoFar: 10 }],
  ])('rejects a first window with %s', (_name, fields) => {
    expect(decideSealedWindowAnswer(ok(fields), first)).toEqual({ kind: 'incoherent' })
  })

  it.each([
    ['a backwards cursor', { text: 'abcd', nextOffset: 12, totalLengthSoFar: 40 }],
    ['an unmoved cursor while bytes remain', { text: '', nextOffset: 16, totalLengthSoFar: 40 }],
    ['a different sealed length', { text: 'abcd', nextOffset: 20, totalLengthSoFar: 41 }],
    ['a shorter sealed length ending at the cursor', { text: 'abcd', nextOffset: 20, totalLengthSoFar: 20 }],
    ['a total below the requested offset', { text: '', nextOffset: 16, totalLengthSoFar: 8 }],
  ])('rejects a later window with %s', (_name, fields) => {
    expect(decideSealedWindowAnswer(ok(fields), later)).toEqual({ kind: 'incoherent' })
  })

  it('maps each closed non-Ok status to its own disclosure whatever its envelope carries', () => {
    // An integrity mismatch legitimately reports the observed file length; a non-Ok envelope is never required to be zero-length.
    expect(decideSealedWindowAnswer(wire({ status: 'IntegrityMismatch', text: '', nextOffset: 16, totalLengthSoFar: 999 }), later)).toEqual({
      kind: 'integrity-mismatch',
    })
    expect(decideSealedWindowAnswer(wire({ status: 'Missing', text: 'ignored', nextOffset: 0, totalLengthSoFar: 0 }), first)).toEqual({ kind: 'missing' })
    expect(decideSealedWindowAnswer(wire({ status: 'AttemptIdentityInvalid' }), first)).toEqual({ kind: 'identity-invalid' })
    for (const status of ['NoAgentEvidence', 'AttemptLinkBroken', 'PurposeNotAllowlisted', 'ArtifactNotFound']) {
      expect(decideSealedWindowAnswer(wire({ status, text: '', nextOffset: 0, totalLengthSoFar: 0 }), first)).toEqual({ kind: 'unavailable' })
    }
  })

  it.each([
    ['an unknown status', wire({ status: 'Partial', text: 'abcd', nextOffset: 4, totalLengthSoFar: 4 })],
    ['a lower-case status', wire({ status: 'ok', text: 'abcd', nextOffset: 4, totalLengthSoFar: 4 })],
    ['an absent status', wire({ text: 'abcd', nextOffset: 4, totalLengthSoFar: 4 })],
    ['an inherited status name', wire({ status: 'toString' })],
    ['no answer', null],
    ['an undefined answer', undefined],
  ])('treats %s as incoherent', (_name, response) => {
    expect(decideSealedWindowAnswer(response, first)).toEqual({ kind: 'incoherent' })
  })
})
