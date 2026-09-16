import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { ProviderRuntimePreflightResponse } from '../../../api/clients'
import { ProviderRuntimePreflight } from './ProviderRuntimePreflight'

function provider(overrides: Partial<ProviderRuntimePreflightResponse> = {}): ProviderRuntimePreflightResponse {
  return {
    provider: 'Codex',
    status: 'Available',
    observedVersion: '1.2.3',
    evidenceObservedAtUtc: new Date('2026-09-16T12:00:00Z'),
    evidenceFreshness: 'Fresh',
    reasonCode: 'provider_runtime.version_observed',
    reasonMessage: 'Runtime version was observed.',
    authentication: 'Unknown',
    modelCatalog: 'Unknown',
    reasoningEffort: 'Unknown',
    permissionMode: 'Unknown',
    contextUsage: 'Unknown',
    compaction: 'Unknown',
    sessions: 'Unknown',
    accountUsage: 'Unknown',
    ...overrides,
  } as ProviderRuntimePreflightResponse
}

describe('ProviderRuntimePreflight', () => {
  it('labels version observation without claiming authentication or invocation readiness', () => {
    render(
      <ProviderRuntimePreflight
        providers={[provider(), provider({ provider: 'ClaudeCode', status: 'Unavailable', observedVersion: undefined })]}
        loading={false}
        error={null}
        refreshingCapability={null}
        onRefresh={vi.fn()}
      />,
    )

    expect(screen.getByText('Version observation does not prove authentication or invocation readiness.')).toBeInTheDocument()
    expect(screen.getByText('Version observed')).toBeInTheDocument()
    expect(screen.getByText('Unavailable')).toBeInTheDocument()
    expect(screen.getAllByText('Authentication and runtime controls: Unknown.')).toHaveLength(2)
    expect(screen.queryByText(/Model catalog:/)).not.toBeInTheDocument()
  })

  it('shows checking and stale attention states using the established readiness language', () => {
    render(
      <ProviderRuntimePreflight
        providers={[
          provider({ status: 'Checking', evidenceFreshness: 'NotObserved', observedVersion: undefined }),
          provider({ provider: 'ClaudeCode', status: 'NeedsAttention', evidenceFreshness: 'Stale' }),
        ]}
        loading={false}
        error={null}
        refreshingCapability={null}
        onRefresh={vi.fn()}
      />,
    )

    expect(screen.getByText('Checking…')).toBeInTheDocument()
    expect(screen.getByText('Needs attention (stale)')).toBeInTheDocument()
  })

  it('refreshes the corresponding existing host capability', () => {
    const onRefresh = vi.fn()
    render(
      <ProviderRuntimePreflight
        providers={[provider({ provider: 'ClaudeCode' })]}
        loading={false}
        error={null}
        refreshingCapability={null}
        onRefresh={onRefresh}
      />,
    )

    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }))

    expect(onRefresh).toHaveBeenCalledWith('ClaudeCli')
  })
})
