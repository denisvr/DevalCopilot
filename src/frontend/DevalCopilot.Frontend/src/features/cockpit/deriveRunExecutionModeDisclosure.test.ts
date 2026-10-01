import { describe, expect, it } from 'vitest'
import { deriveRunExecutionModeDisclosure } from './deriveRunExecutionModeDisclosure'

describe('deriveRunExecutionModeDisclosure', () => {
  it('labels a manual Agent run and waits for an explicit planning request while Created', () => {
    const created = deriveRunExecutionModeDisclosure('ManualAgent', 'Created')
    expect(created.label).toBe('Manual Agent run')
    expect(created.agentActionsAllowed).toBe(true)
    expect(created.waitingHint).toBe('Waiting for an explicit planning request. Nothing runs until one is requested.')
    expect(created.waitingHint).not.toMatch(/autonomous|ready/i)
    expect(deriveRunExecutionModeDisclosure('ManualAgent', 'Running').waitingHint).toBeNull()
  })

  it('keeps a legacy run visibly unclassified but Agent-capable', () => {
    const legacy = deriveRunExecutionModeDisclosure('Legacy', 'Created')
    expect(legacy.label).toBe('Legacy run — execution mode was not recorded')
    expect(legacy.agentActionsAllowed).toBe(true)
    expect(legacy.waitingHint).toBeNull()
  })

  it('labels a simulated run as a demo and withholds Agent actions', () => {
    const simulated = deriveRunExecutionModeDisclosure('Simulated', 'Running')
    expect(simulated.label).toBe('Simulated demo run')
    expect(simulated.agentActionsAllowed).toBe(false)
    expect(simulated.agentActionsNote).toMatch(/simulated demo run/i)
  })

  it.each(['Unrecognized', 'Future', '', undefined, null])('treats %s as unrecognized and withholds Agent actions', (mode) => {
    const d = deriveRunExecutionModeDisclosure(mode as string | undefined)
    expect(d.kind).toBe('Unrecognized')
    expect(d.agentActionsAllowed).toBe(false)
    expect(d.label).toMatch(/unrecognized/i)
    expect(d.agentActionsNote).toMatch(/not recognize/i)
  })

  it('is case-sensitive: a mis-cased mode is not guessed into a known one', () => {
    expect(deriveRunExecutionModeDisclosure('manualagent').agentActionsAllowed).toBe(false)
  })
})
