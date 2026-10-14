namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckContextsObservation;

/// <summary>The first bounded page of a commit's contexts: GitHub's total count and the returned entries in provider order, at most
/// <see cref="GitHubCheckContextsPolicy.MaxContexts"/> of them and exactly as many as the lesser of that bound and the total.</summary>
public sealed record GitHubCheckContextsPage(int TotalCount, IReadOnlyList<GitHubCheckContext> Contexts);
