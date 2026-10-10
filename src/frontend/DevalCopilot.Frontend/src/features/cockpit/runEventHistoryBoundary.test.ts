import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

// ADR-0034: the recorded-events region is read-only and historical. It mounts none of the live cockpit, eligibility, configuration or
// mutation hooks, reuses neither the live event reader nor the collaboration timeline, offers no timer, persistence, navigation or
// payload viewer, never converts a sequence through Number, and owns its lifetimes through the shared owned-lifetime hooks rather
// than a key or remount workaround.
const here = (relative: string) => fileURLToPath(new URL(relative, import.meta.url))
const read = (relative: string) =>
  readFileSync(here(relative), 'utf8')
    .split('\n')
    .filter((line) => !line.trimStart().startsWith('//') && !line.trimStart().startsWith('*') && !line.trimStart().startsWith('/*'))
    .join('\n')

const SOURCES = ['./runEventHistory.ts', './hooks/useRunEventHistory.ts', './components/RunEventHistoryPanel.tsx']

describe('the recorded events region boundary', () => {
  it.each(SOURCES)('%s mounts no live cockpit, eligibility, configuration, mutation or live-event code', (source) => {
    const code = read(source)

    for (const forbidden of [
      'RunCockpitView', 'useRunCockpit', 'useLocalCommitStatus', 'LocalCommitPanel', 'useEvidenceRefreshConnection', 'useProjectWorkspace',
      'useProjectGitEvidence', 'CheckpointReviewPanel', 'AbandonRunPanel', 'RunIntakeForm', 'runCockpitClient', 'localCommitStatusClient',
      'createManualRunClient', 'startSimulatedRunClient', 'requestLocalCommitClient', 'abandonManualRunClient', 'prepareWorkspaceClient',
      'recordCheckpointReviewClient', 'claimVerificationExecutionClient', 'environmentClient', 'authenticatedHttp', 'fetch(',
      'runEventsClient', 'useCollaborationTimeline', 'collaborationTimelineClient', 'parseEventSummary', 'signalr', 'runNotifications',
      'payloadJson', 'attemptId', 'JSON.parse',
    ]) {
      expect(code, `${source} must not reference ${forbidden}`).not.toContain(forbidden)
    }
  })

  it.each(SOURCES)('%s has no timer, polling, browser persistence, navigation or markup injection', (source) => {
    const code = read(source)

    for (const forbidden of [
      'setInterval', 'setTimeout', 'requestAnimationFrame', 'localStorage', 'sessionStorage', 'indexedDB', 'document.cookie',
      'window.location', 'history.pushState', 'dangerouslySetInnerHTML', 'innerHTML', 'new Date()', 'Date.now',
    ]) {
      expect(code, `${source} must not use ${forbidden}`).not.toContain(forbidden)
    }
  })

  it('never converts a sequence or cursor through a Number', () => {
    for (const source of SOURCES) {
      const code = read(source)

      // `Number.isNaN` only validates a Date's time value; no sequence or cursor text is ever parsed as a Number.
      for (const forbidden of ['Number(', 'parseInt', 'parseFloat', 'Math.', 'Number.parse', 'Number.isSafeInteger', 'Number.isInteger']) {
        expect(code, `${source} must not use ${forbidden}`).not.toContain(forbidden)
      }
    }
  })

  it('reads events through the generated client factory and owns its lifetimes with the shared owned-lifetime hooks', () => {
    const hook = read('./hooks/useRunEventHistory.ts')

    expect(hook).toContain('runEventHistoryClient()')
    expect(hook).toContain('useOwnedLifetime')
    expect(hook).toContain('useOwnedState')
    expect(hook).not.toContain('useRef(0)')
    expect(read('./components/ProjectRunHistoryDetail.tsx')).toContain('<RunEventHistoryPanel runId={entry.runId} />')
  })

  it('is composed once in the historical detail without a key or remount workaround, and the existing readers are untouched', () => {
    const detail = read('./components/ProjectRunHistoryDetail.tsx')

    expect(detail.match(/<RunEventHistoryPanel/g)).toHaveLength(1)
    expect(detail).not.toMatch(/<RunEventHistoryPanel[^>]*\bkey=/)
    expect(read('../../App.tsx')).not.toContain('RunEventHistoryPanel')
    for (const untouched of ['./hooks/useCollaborationTimeline.ts', './hooks/useLocalDeliveryReceipt.ts', './projectRunHistory.ts']) {
      expect(read(untouched), `${untouched} must not know the event history`).not.toMatch(/runEventHistory|RunEventHistory/)
    }
  })

  it('keeps the decoder\'s sequence comparison on exact integers', () => {
    const decoder = read('./runEventHistory.ts')

    expect(decoder).toContain('BigInt(')
    expect(decoder).toContain('CANONICAL_SEQUENCE')
    expect(decoder).toContain('MAX_SEQUENCE')
  })
})
