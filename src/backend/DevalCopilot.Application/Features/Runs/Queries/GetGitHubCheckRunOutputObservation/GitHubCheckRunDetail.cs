namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckRunOutputObservation;

/// <summary>The fields only the output phase selects: the check run's name, literal status, literal conclusion (or null) and output.</summary>
public sealed record GitHubCheckRunDetail(string Name, string Status, string? Conclusion, GitHubCheckRunOutputText Output);
