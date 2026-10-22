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
  // HIST-ATT-01: the Agent-attempt history composed into the selected run, with its evidence and sealed-window reads.
  './agentAttemptHistory.ts',
  './hooks/useAgentAttemptHistory.ts',
  './hooks/useAgentAttemptEvidence.ts',
  './hooks/useAgentAttemptArtifactWindow.ts',
  './hooks/useSealedArtifactWindow.ts',
  './components/AgentAttemptHistoryPanel.tsx',
  './components/AgentAttemptArtifactViewer.tsx',
  './components/SealedArtifactWindowView.tsx',
  // HIST-INP-01: the selected attempt's on-demand recorded collaboration inputs.
  './agentAttemptInputs.ts',
  './agentAttemptInputsWire.ts',
  './hooks/useAgentAttemptInputs.ts',
  './components/SelectedAttemptRecordedInputs.tsx',
  // HIST-INPC-01: the explicit content read of one selected recorded input.
  './agentAttemptInputMessage.ts',
  './agentAttemptInputMessageWire.ts',
  './hooks/useAgentAttemptInputMessage.ts',
  './components/SelectedAttemptInputMessage.tsx',
  // HIST-OUT-01: the selected attempt's recorded outputs and the explicit content read of one of them.
  './agentAttemptOutputs.ts',
  './agentAttemptOutputsWire.ts',
  './agentAttemptOutputMessage.ts',
  './agentAttemptOutputMessageWire.ts',
  './hooks/useAgentAttemptOutputs.ts',
  './hooks/useAgentAttemptOutputMessage.ts',
  './components/SelectedAttemptRecordedOutputs.tsx',
  './components/SelectedAttemptOutputMessage.tsx',
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

  it('composes the existing Agent-attempt history into the selected run without a key, owning every read with the shared hooks', () => {
    const detail = read('./components/ProjectRunHistoryDetail.tsx')

    expect(detail).toContain('<AgentAttemptHistoryPanel runId={entry.runId} />')
    expect(detail).not.toMatch(/<AgentAttemptHistoryPanel[^>]*\bkey=/)
    for (const hook of ['./hooks/useAgentAttemptHistory.ts', './hooks/useAgentAttemptEvidence.ts', './hooks/useSealedArtifactWindow.ts']) {
      expect(read(hook), hook).toContain('useOwnedLifetime')
      expect(read(hook), hook).toContain('useOwnedState')
      expect(read(hook), hook).not.toContain('useRef(0)')
    }

    expect(read('./hooks/useAgentAttemptHistory.ts')).toContain('agentAttemptHistoryClient()')
    expect(read('./hooks/useAgentAttemptEvidence.ts')).toContain('agentAttemptEvidenceClient()')
    expect(read('./hooks/useAgentAttemptArtifactWindow.ts')).toContain('agentAttemptArtifactWindowClient()')
    expect(read('./components/AgentAttemptHistoryPanel.tsx')).not.toMatch(/\bkey=\{`/)
    // ADR-0037's minimal attribution inspector stays an independent path: it neither mounts nor reads attempt evidence.
    const collaboration = read('./components/CollaborationHistoryPanel.tsx')
    expect(collaboration).not.toContain('AgentAttemptHistoryPanel')
    expect(collaboration).not.toContain('useAgentAttemptEvidence')
  })

  it('reads recorded inputs only through their factory, owns them with the shared hooks and keeps them apart from evidence and artifacts', () => {
    const hook = read('./hooks/useAgentAttemptInputs.ts')
    const section = read('./components/SelectedAttemptRecordedInputs.tsx')
    // The detail (extracted unchanged from the panel for ADR-0061 reuse) owns the section mounts.
    const panel = read('./components/AgentAttemptDetail.tsx')

    expect(hook).toContain('agentAttemptInputsClient()')
    expect(hook).toContain('useOwnedLifetime')
    expect(hook).toContain('useOwnedState')
    expect(hook).not.toContain('useRef(0)')
    for (const forbidden of ['agentAttemptEvidenceClient', 'agentAttemptArtifactWindowClient', 'collaborationMessageEvidenceClient', 'console.']) {
      expect(hook, forbidden).not.toContain(forbidden)
      expect(section, forbidden).not.toContain(forbidden)
    }

    expect(section).not.toContain('useAgentAttemptEvidence')
    expect(section).not.toContain('useAgentAttemptArtifactWindow')
    expect(panel).toContain('<SelectedAttemptRecordedInputs selectionKey={selectionKey} runId={runId} expected={expected} />')
    expect(panel).not.toMatch(/<SelectedAttemptRecordedInputs[^>]*\bkey=/)
  })

  it('reads one selected input message only through its factory, scoped by the shared hooks, and follows no chain or artifact', () => {
    const hook = read('./hooks/useAgentAttemptInputMessage.ts')
    const inspector = read('./components/SelectedAttemptInputMessage.tsx')
    const section = read('./components/SelectedAttemptRecordedInputs.tsx')

    expect(hook).toContain('agentAttemptInputMessageClient()')
    expect(hook).toContain('useOwnedLifetime')
    expect(hook).toContain('useOwnedState')
    expect(hook).not.toContain('useRef(0)')
    for (const forbidden of [
      'agentAttemptEvidenceClient', 'agentAttemptArtifactWindowClient', 'collaborationMessageEvidenceClient', 'collaborationReplyClient',
      'collaborationAttemptClient', 'collaborationHistoryClient', 'console.', '<a ', 'href', 'download',
    ]) {
      expect(hook, forbidden).not.toContain(forbidden)
      expect(inspector, forbidden).not.toContain(forbidden)
      expect(section, forbidden).not.toContain(forbidden)
    }

    // The section asks for the whole bounded set once per opening, and content only through the explicit selection.
    expect(read('./hooks/useAgentAttemptInputs.ts')).toContain('.readAgentAttemptInputs(runId, attemptId, AGENT_ATTEMPT_INPUTS_MAX_LIMIT)')
    expect(section).toContain('useAgentAttemptInputMessage({ openingKey, runId, expected, selection })')
    expect(section.match(/useAgentAttemptInputMessage\(/g)).toHaveLength(1)
    expect(section).not.toMatch(/\bkey=\{(?!reference\.collaborationMessageId\})/)
  })

  it('reads recorded outputs and one selected output only through their factories, scoped by the shared hooks, and follows no chain or artifact', () => {
    const outputs = read('./hooks/useAgentAttemptOutputs.ts')
    const message = read('./hooks/useAgentAttemptOutputMessage.ts')
    const inspector = read('./components/SelectedAttemptOutputMessage.tsx')
    const section = read('./components/SelectedAttemptRecordedOutputs.tsx')
    // The detail (extracted unchanged from the panel for ADR-0061 reuse) owns the section mounts.
    const panel = read('./components/AgentAttemptDetail.tsx')

    expect(outputs).toContain('agentAttemptOutputsClient()')
    expect(message).toContain('agentAttemptOutputMessageClient()')
    for (const hook of [outputs, message]) {
      expect(hook).toContain('useOwnedLifetime')
      expect(hook).toContain('useOwnedState')
      expect(hook).not.toContain('useRef(0)')
    }

    for (const forbidden of [
      'agentAttemptEvidenceClient', 'agentAttemptArtifactWindowClient', 'collaborationMessageEvidenceClient', 'collaborationReplyClient',
      'collaborationAttemptClient', 'collaborationHistoryClient', 'agentAttemptInputsClient', 'agentAttemptInputMessageClient',
      'console.', '<a ', 'href', 'download', 'useAgentAttemptEvidence', 'useAgentAttemptArtifactWindow', 'useAgentAttemptInputs',
    ]) {
      for (const code of [outputs, message, inspector, section]) {
        expect(code, forbidden).not.toContain(forbidden)
      }
    }

    // The section asks for the whole bounded set once per opening with no limit, and content only through the explicit selection.
    expect(outputs).toContain('.readAgentAttemptOutputs(runId, attemptId)')
    expect(outputs).not.toContain('limit')
    expect(section).toContain('useAgentAttemptOutputMessage({ openingKey, runId, expected, selection })')
    expect(section.match(/useAgentAttemptOutputMessage\(/g)).toHaveLength(1)
    expect(section).not.toMatch(/\bkey=\{(?!reference\.collaborationMessageId\})/)
    expect(panel).toContain('<SelectedAttemptRecordedOutputs selectionKey={selectionKey} runId={runId} expected={expected} />')
    expect(panel).not.toMatch(/<SelectedAttemptRecordedOutputs[^>]*\bkey=/)
  })

  it('is composed once in the app without a key or remount workaround, and the receipt decoder and endpoint are untouched', () => {
    const app = read('../../App.tsx')

    expect(app.match(/<ProjectRunHistoryPanel/g)).toHaveLength(1)
    expect(app).not.toMatch(/<ProjectRunHistoryPanel[^>]*\bkey=/)
    expect(read('./hooks/useLocalDeliveryReceipt.ts')).toContain('localDeliveryReceiptClient()')
    expect(read('./localDeliveryReceipt.ts')).not.toContain('projectRunHistory')
  })
})
