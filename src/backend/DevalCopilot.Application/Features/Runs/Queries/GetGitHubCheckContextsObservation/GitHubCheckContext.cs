namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckContextsObservation;

/// <summary>
/// One check run or commit status exactly as GitHub returned it: the closed kind, its opaque identifier, its label, and the literal
/// status and conclusion values, independent of each other. A status context carries its state as <c>Status</c> and has no conclusion or
/// check suite. Nothing is normalized, paired, deduplicated or interpreted.
/// </summary>
public sealed record GitHubCheckContext(string Kind, string Id, string Name, string Status, string? Conclusion, string? CheckSuiteId);
