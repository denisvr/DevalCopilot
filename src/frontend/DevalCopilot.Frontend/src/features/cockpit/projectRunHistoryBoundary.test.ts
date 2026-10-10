import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

// ADR-0033: the history region is read-only and historical. It mounts none of the live cockpit, eligibility, configuration or
// mutation hooks, offers no timer, persistence or navigation, and owns its lifetimes through the shared owned-lifetime hooks
// rather than a key or remount workaround.
const here = (relative: string) => fileURLToPath(new URL(relative, import.meta.url))
const read = (relative: string) =>
  readFileSync(here(relative), 'utf8')
    .split('\n')
    .filter((line) => !line.trimStart().startsWith('//') && !line.trimStart().startsWith('*') && !line.trimStart().startsWith('/*'))
    .join('\n')

const SOURCES = [
  './projectRunHistory.ts',
  './hooks/useProjectRunHistory.ts',
  './components/ProjectRunHistoryPanel.tsx',
  './components/ProjectRunHistoryDetail.tsx',
]

describe('the run history region boundary', () => {
  it.each(SOURCES)('%s mounts no live cockpit, eligibility, configuration or mutation code', (source) => {
    const code = read(source)

    for (const forbidden of [
      'RunCockpitView', 'useRunCockpit', 'useLocalCommitStatus', 'LocalCommitPanel', 'useEvidenceRefreshConnection', 'useProjectWorkspace',
      'useProjectGitEvidence', 'CheckpointReviewPanel', 'AbandonRunPanel', 'RunIntakeForm', 'runCockpitClient', 'localCommitStatusClient',
      'createManualRunClient', 'startSimulatedRunClient', 'requestLocalCommitClient', 'abandonManualRunClient', 'prepareWorkspaceClient',
      'recordCheckpointReviewClient', 'claimVerificationExecutionClient', 'environmentClient', 'authenticatedHttp', 'fetch(',
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

  it('reads history through the generated client factory and owns its lifetimes with the shared owned-lifetime hooks', () => {
    const hook = read('./hooks/useProjectRunHistory.ts')

    expect(hook).toContain('projectRunHistoryClient()')
    expect(hook).toContain('useOwnedLifetime')
    expect(hook).toContain('useOwnedState')
    expect(hook).not.toContain('useRef(0)')
    expect(read('./components/ProjectRunHistoryDetail.tsx')).toContain('<LocalDeliveryReceipt source={source} />')
  })

  it('is composed once in the app without a key or remount workaround, and the receipt decoder and endpoint are untouched', () => {
    const app = read('../../App.tsx')

    expect(app.match(/<ProjectRunHistoryPanel/g)).toHaveLength(1)
    expect(app).not.toMatch(/<ProjectRunHistoryPanel[^>]*\bkey=/)
    expect(read('./hooks/useLocalDeliveryReceipt.ts')).toContain('localDeliveryReceiptClient()')
    expect(read('./localDeliveryReceipt.ts')).not.toContain('projectRunHistory')
  })
})
