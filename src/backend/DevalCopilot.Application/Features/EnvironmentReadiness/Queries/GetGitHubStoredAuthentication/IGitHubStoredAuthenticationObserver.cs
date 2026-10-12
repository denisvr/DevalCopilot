namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetGitHubStoredAuthentication;

/// <summary>
/// Observes, once and read-only, the default stored GitHub CLI authentication of github.com through one
/// already-vetted direct executable. It grants no repository permission or publication authority, never
/// reads credentials, and every unusable outcome (timeout, failure, malformed or oversized output, a
/// concurrent probe) resolves to <see cref="GitHubStoredAuthenticationObservation.Unknown"/>; only the
/// caller's own cancellation is raised as <see cref="OperationCanceledException"/>.
/// </summary>
public interface IGitHubStoredAuthenticationObserver
{
    Task<GitHubStoredAuthenticationObservation> ObserveAsync(string executablePath, CancellationToken cancellationToken);
}
