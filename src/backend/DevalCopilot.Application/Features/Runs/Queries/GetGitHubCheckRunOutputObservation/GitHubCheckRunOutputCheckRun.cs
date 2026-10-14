namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckRunOutputObservation;

/// <summary>The admitted check run: its identifier (equal to the request), its check suite, the commit of that suite (equal to the
/// recorded commit), its safe name, its literal status and its literal conclusion (or null), and its output, which is
/// <see langword="null"/> exactly when title, summary and text were all null. Status and conclusion are independent; nothing is paired or
/// interpreted.</summary>
public sealed record GitHubCheckRunOutputCheckRun(
    string Id,
    string CheckSuiteId,
    string CommitSha,
    string Name,
    string Status,
    string? Conclusion,
    GitHubCheckRunOutputText? Output);
