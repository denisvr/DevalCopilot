import { describe, expect, it } from 'vitest'
import { toUtcText } from './utcText'

describe('toUtcText', () => {
  it('renders a revived Date as ISO text, so it can be a React child', () => {
    expect(toUtcText(new Date('2026-09-30T10:00:00.000Z'))).toBe('2026-09-30T10:00:00.000Z')
  })

  it('passes a wire string through and maps missing or invalid values to empty text', () => {
    expect(toUtcText('2026-09-30T10:00:00Z')).toBe('2026-09-30T10:00:00Z')
    expect(toUtcText(undefined)).toBe('')
    expect(toUtcText(new Date('invalid'))).toBe('')
  })
})
