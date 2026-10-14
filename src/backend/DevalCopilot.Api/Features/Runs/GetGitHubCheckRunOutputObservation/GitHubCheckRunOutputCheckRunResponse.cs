namespace DevalCopilot.Api.Features.Runs.GetGitHubCheckRunOutputObservation;

/// <summary>The admitted check run exactly as GitHub returned it, as safe post-redaction text: its identifier (equal to the request), its
/// check suite, the commit of that suite (equal to the recorded commit), its name, its literal status, its literal conclusion or null (not
/// returned) and its output, which is null exactly when title, summary and text were all null. Nothing is paired, ordered or interpreted.</summary>
public sealed record GitHubCheckRunOutputCheckRunResponse(
    string Id,
    string CheckSuiteId,
    string CommitSha,
    string Name,
    string Status,
    string? Conclusion,
    GitHubCheckRunOutputTextResponse? Output);
