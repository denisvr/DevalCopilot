namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>
/// Provider-neutral, provider-reported token usage for one Agent invocation, shared by every
/// Agent adapter port regardless of provider. An adapter constructs it only from a usage report it
/// parsed through a proven, versioned provider contract (<paramref name="SchemaVersion"/> names
/// that contract); an adapter whose provider has no proven contract, or whose provider did not
/// report usage in a well-shaped form, reports no usage at all rather than an inferred value — it
/// is never derived from output length, configured limits, or another provider's fields. It never
/// carries a path, argument, environment value, output, session identifier, or credential, and it
/// is never a semantic classification of the provider's response.
/// </summary>
public sealed record AgentTokenUsage(
    int InputTokens,
    int OutputTokens,
    int? CacheCreationInputTokens,
    int? CacheReadInputTokens,
    string SchemaVersion);
