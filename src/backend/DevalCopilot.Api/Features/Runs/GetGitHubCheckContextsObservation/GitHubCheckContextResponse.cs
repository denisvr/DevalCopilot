namespace DevalCopilot.Api.Features.Runs.GetGitHubCheckContextsObservation;

/// <summary>One context exactly as GitHub returned it. <c>Kind</c> is CheckRun or StatusContext. A status context carries its literal
/// state as <c>Status</c> and has a null <c>Conclusion</c> and <c>CheckSuiteId</c>; a check run carries its literal status, a literal
/// conclusion or null (not yet returned) and its check suite identifier. Nothing is paired, ordered or interpreted.</summary>
public sealed record GitHubCheckContextResponse(
    string Kind, string Id, string Name, string Status, string? Conclusion, string? CheckSuiteId);
