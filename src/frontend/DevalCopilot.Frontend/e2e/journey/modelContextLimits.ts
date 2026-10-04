// What the native Claude double lists for each of its three print contracts (tests/.../ProviderFixture/ClaudeRole.cs), written here
// independently of the host's code: the models a stage must have been recorded with, in the ordinal order of their identifiers (the
// order the host stores and the page shows, not the order the double listed them), and the exact canonical text the host persists.
// The values differ by contract so a limit can never be mistaken for another stage's, and none of them is a default.

export interface ExpectedModelLimit {
  modelId: string
  contextWindowTokens: number
  maxOutputTokens: number
}

export const EXPECTED_MODEL_LIMITS: Readonly<Record<string, readonly ExpectedModelLimit[]>> = {
  CriticalReview: [{ modelId: 'claude-journey-critic', contextWindowTokens: 180000, maxOutputTokens: 24000 }],
  ImplementationReport: [
    { modelId: 'claude-journey-impl-helper', contextWindowTokens: 200000, maxOutputTokens: 32000 },
    { modelId: 'claude-journey-impl-main', contextWindowTokens: 1000000, maxOutputTokens: 64000 },
  ],
  ReviewCorrection: [{ modelId: 'claude-journey-fix', contextWindowTokens: 150000, maxOutputTokens: 16000 }],
}

/** The one canonical text the host persists for these models: snapshot version, parsing-contract source, entries by identifier. */
export function canonicalSnapshot(models: readonly ExpectedModelLimit[]): string {
  return JSON.stringify({
    version: 1,
    source: 'claude-cli-model-usage-v1',
    models: models.map((model) => ({
      modelId: model.modelId,
      contextWindowTokens: model.contextWindowTokens,
      maxOutputTokens: model.maxOutputTokens,
    })),
  })
}

/** The rows the page renders for these models: identifier, then the two limits with thousands separators. */
export function renderedRows(models: readonly ExpectedModelLimit[]): string[][] {
  return models.map((model) => [
    model.modelId,
    model.contextWindowTokens.toLocaleString('en-US'),
    model.maxOutputTokens.toLocaleString('en-US'),
  ])
}

/** What the evidence response (as the generated client received it) must carry for these models, member for member. */
export function responseMember(models: readonly ExpectedModelLimit[]): { models: ExpectedModelLimit[] } {
  return { models: models.map((model) => ({ ...model })) }
}
