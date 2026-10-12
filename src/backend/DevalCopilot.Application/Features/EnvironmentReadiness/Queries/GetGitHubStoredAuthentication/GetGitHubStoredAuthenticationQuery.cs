using Devalente.Shared.Cqrs;

namespace DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetGitHubStoredAuthentication;

/// <summary>
/// Requests one explicit observation of the default stored GitHub CLI authentication for github.com
/// (ADR-0038). It discovers, refreshes and installs nothing: only a current, coherent, direct-executable
/// GitHubCli discovery snapshot is ever invoked. It never carries a host or any other client choice.
/// </summary>
public sealed record GetGitHubStoredAuthenticationQuery : IQuery<GetGitHubStoredAuthenticationQueryResult>;
