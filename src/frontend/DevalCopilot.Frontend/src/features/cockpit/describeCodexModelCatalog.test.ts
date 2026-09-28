import { describe, expect, it } from 'vitest'
import { describeCodexModelCatalog, describeCodexModelCatalogRetrievedAt } from './describeCodexModelCatalog'

describe('describeCodexModelCatalog', () => {
  it('renders an explicit Unknown for a null catalog', () => {
    expect(describeCodexModelCatalog(null)).toEqual(['Codex model catalog: Unknown'])
  })

  it('renders an explicit Unknown for an Unknown status', () => {
    expect(
      describeCodexModelCatalog({
        status: 'Unknown',
        models: [{ id: 'gpt-6-sol', displayName: 'GPT-6 Sol' }],
      }),
    ).toEqual(['Codex model catalog: Unknown'])
  })

  it('renders an explicit Unknown when Observed carries no models', () => {
    expect(describeCodexModelCatalog({ status: 'Observed', models: [] })).toEqual(['Codex model catalog: Unknown'])
  })

  it('renders one line per model, without an invented aggregate across models', () => {
    const lines = describeCodexModelCatalog({
      status: 'Observed',
      models: [
        { id: 'gpt-6-sol', displayName: 'GPT-6 Sol', supportedReasoningEfforts: ['medium', 'high'], defaultReasoningEffort: 'medium' },
        { id: 'gpt-6-mini', displayName: 'GPT-6 Mini', supportedReasoningEfforts: ['low'] },
      ],
    })

    expect(lines).toHaveLength(2)
    expect(lines[0]).toContain('GPT-6 Sol')
    expect(lines[0]).toContain('medium, high')
    expect(lines[0]).toContain('default: medium')
    expect(lines[1]).toContain('GPT-6 Mini')
    expect(lines[1]).toContain('default: Unknown')
  })

  it('falls back to the model id when a display name is missing', () => {
    const lines = describeCodexModelCatalog({ status: 'Observed', models: [{ id: 'gpt-6-sol' }] })

    expect(lines[0]).toMatch(/^gpt-6-sol/)
  })

  it('shows Unknown effort fields for a model with no supported efforts reported', () => {
    const lines = describeCodexModelCatalog({ status: 'Observed', models: [{ id: 'gpt-6-sol', displayName: 'GPT-6 Sol' }] })

    expect(lines[0]).toContain('efforts: Unknown')
    expect(lines[0]).toContain('default: Unknown')
  })

  it('shows None (not Unknown) when the model genuinely reports an empty supported-efforts list', () => {
    const lines = describeCodexModelCatalog({
      status: 'Observed',
      models: [{ id: 'gpt-6-sol', displayName: 'GPT-6 Sol', supportedReasoningEfforts: [] }],
    })

    expect(lines[0]).toContain('efforts: None')
  })

  it('shows Unknown (not None or a partial list) when the reported supported-efforts field itself is null', () => {
    const lines = describeCodexModelCatalog({
      status: 'Observed',
      models: [{ id: 'gpt-6-sol', displayName: 'GPT-6 Sol', supportedReasoningEfforts: null }],
    })

    expect(lines[0]).toContain('efforts: Unknown')
  })
})

describe('describeCodexModelCatalogRetrievedAt', () => {
  it('returns null when the catalog is Unknown', () => {
    expect(describeCodexModelCatalogRetrievedAt({ status: 'Unknown' })).toBeNull()
  })

  it('returns null when the catalog has no retrieval time', () => {
    expect(describeCodexModelCatalogRetrievedAt({ status: 'Observed' })).toBeNull()
  })

  it('formats a real retrieval time for an Observed catalog', () => {
    const result = describeCodexModelCatalogRetrievedAt({ status: 'Observed', retrievedAtUtc: '2026-09-28T12:00:00Z' })
    expect(result).toMatch(/^Retrieved /)
  })
})
