namespace DevalCopilot.Domain.Features.Runs;

/// <summary>
/// Provider-reported token usage for one Agent attempt's provider invocation: input and output
/// token counts, optional cache-creation and cache-read input token counts, and the fixed
/// parsing-contract <see cref="SchemaVersion"/> that produced them. Unlike
/// <see cref="AgentProcessExecutionEvidence"/>, this is reported by the provider rather than
/// measured by the host, and it is best-effort observation only — never a semantic classification
/// of the attempt (that remains <see cref="AgentOutcome"/>) and never a precondition for any
/// outcome. An attempt whose provider did not report usage in a proven, safely parsed contract has
/// no instance of this type at all — absence is never replaced by an inferred or invented value.
/// </summary>
public sealed record AgentTokenUsageEvidence
{
    /// <summary>Matches the existing bound for version-tag strings such as
    /// <see cref="Attempt.AgentAdapterContractVersion"/>.</summary>
    public const int MaxSchemaVersionLength = 128;

    private AgentTokenUsageEvidence(
        int inputTokens, int outputTokens, int? cacheCreationInputTokens, int? cacheReadInputTokens, string schemaVersion)
    {
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        CacheCreationInputTokens = cacheCreationInputTokens;
        CacheReadInputTokens = cacheReadInputTokens;
        SchemaVersion = schemaVersion;
    }

    public int InputTokens { get; }

    public int OutputTokens { get; }

    /// <summary>Null when the provider's contract has no cache-creation breakdown.</summary>
    public int? CacheCreationInputTokens { get; }

    /// <summary>Null when the provider's contract has no cache-read breakdown.</summary>
    public int? CacheReadInputTokens { get; }

    /// <summary>The adapter-owned parsing-contract tag that produced this evidence, so a future
    /// provider schema change is attributable rather than silently reinterpreted.</summary>
    public string SchemaVersion { get; }

    /// <summary>Creates validated evidence, throwing <see cref="ArgumentException"/> for any shape
    /// <see cref="Validate"/> rejects.</summary>
    public static AgentTokenUsageEvidence Create(
        int inputTokens, int outputTokens, int? cacheCreationInputTokens, int? cacheReadInputTokens, string schemaVersion)
    {
        var violation = Validate(inputTokens, outputTokens, cacheCreationInputTokens, cacheReadInputTokens, schemaVersion);
        if (violation is not null)
        {
            throw new ArgumentException($"Invalid agent token-usage evidence: {violation}.", nameof(inputTokens));
        }

        return new AgentTokenUsageEvidence(inputTokens, outputTokens, cacheCreationInputTokens, cacheReadInputTokens, schemaVersion);
    }

    /// <summary>Reconstructs evidence from persisted nullable members, returning
    /// <see langword="null"/> — unknown, never partially trusted — when any required member
    /// (<paramref name="inputTokens"/>, <paramref name="outputTokens"/>,
    /// <paramref name="schemaVersion"/>) is missing, the shape violates <see cref="Validate"/>,
    /// or the persisted provider/schema pair has no proven contract.
    /// The single rule shared by <see cref="Attempt.GetAgentTokenUsageEvidence"/> and every
    /// read-side projection that aggregates persisted usage without materializing attempts.</summary>
    public static AgentTokenUsageEvidence? FromPersisted(
        AgentProvider? provider, int? inputTokens, int? outputTokens, int? cacheCreationInputTokens,
        int? cacheReadInputTokens, string? schemaVersion)
    {
        if (inputTokens is not { } input
            || outputTokens is not { } output
            || schemaVersion is null
            || !AgentTokenUsageEvidencePolicy.IsSupportedSource(provider, schemaVersion)
            || Validate(input, output, cacheCreationInputTokens, cacheReadInputTokens, schemaVersion) is not null)
        {
            return null;
        }

        return new AgentTokenUsageEvidence(input, output, cacheCreationInputTokens, cacheReadInputTokens, schemaVersion);
    }

    /// <summary>Returns the first shape violation, or <see langword="null"/> when the shape is valid.</summary>
    public static AgentTokenUsageEvidenceViolation? Validate(
        int inputTokens, int outputTokens, int? cacheCreationInputTokens, int? cacheReadInputTokens, string? schemaVersion)
    {
        if (inputTokens < 0)
        {
            return AgentTokenUsageEvidenceViolation.NegativeInputTokens;
        }

        if (outputTokens < 0)
        {
            return AgentTokenUsageEvidenceViolation.NegativeOutputTokens;
        }

        if (cacheCreationInputTokens < 0)
        {
            return AgentTokenUsageEvidenceViolation.NegativeCacheCreationInputTokens;
        }

        if (cacheReadInputTokens < 0)
        {
            return AgentTokenUsageEvidenceViolation.NegativeCacheReadInputTokens;
        }

        if (string.IsNullOrWhiteSpace(schemaVersion) || schemaVersion.Length > MaxSchemaVersionLength)
        {
            return AgentTokenUsageEvidenceViolation.InvalidSchemaVersion;
        }

        return null;
    }
}
