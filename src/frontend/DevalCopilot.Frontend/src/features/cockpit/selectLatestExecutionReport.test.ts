import { describe, expect, it } from 'vitest'
import { selectLatestExecutionReportMessageId } from './selectLatestExecutionReport'

describe('selectLatestExecutionReportMessageId', () => {
  it('uses the backend-authoritative initial report while the timeline is still loading', () => {
    expect(selectLatestExecutionReportMessageId('initial', false, null)).toBe('initial')
  })

  it('withholds the target during the initial status load', () => {
    expect(selectLatestExecutionReportMessageId('initial', true, null)).toBeNull()
  })

  it('does not retain a stale target when the authoritative refresh fails', () => {
    expect(selectLatestExecutionReportMessageId('initial', false, 'Review correction status is unavailable.')).toBeNull()
  })

  it('uses the backend winner before and after the timeline catches up', () => {
    expect(selectLatestExecutionReportMessageId('corrected', false, null)).toBe('corrected')
    expect(selectLatestExecutionReportMessageId('corrected', false, null)).toBe('corrected')
  })

  it('uses the winning correction report for InputAlreadyCorrected', () => {
    expect(selectLatestExecutionReportMessageId('winning-correction', false, null)).toBe('winning-correction')
  })

  it('does not fall back to a stale report when no report is reviewable', () => {
    expect(selectLatestExecutionReportMessageId(null, false, null)).toBeNull()
  })
})
