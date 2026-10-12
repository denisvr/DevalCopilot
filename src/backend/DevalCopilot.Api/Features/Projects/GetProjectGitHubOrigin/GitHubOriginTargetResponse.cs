namespace DevalCopilot.Api.Features.Projects.GetProjectGitHubOrigin;

/// <summary>The normalized supported target: the fixed host <c>github.com</c> and the validated owner and repository.</summary>
public sealed record GitHubOriginTargetResponse(string Host, string Owner, string Repository);
