using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubRepositoryIdentityBinding;

/// <summary>Read-only state of a run's recorded GitHub repository identity binding (ADR-0043). It reads persisted rows only through untracked
/// projections: no Git, filesystem, process, provider or network, and it writes, locks and repairs nothing. A recorded binding is
/// historical evidence, never a fresh remote check.</summary>
public sealed record GetGitHubRepositoryIdentityBindingQuery(Guid RunId) : IQuery<Result<GetGitHubRepositoryIdentityBindingQueryResult>>;
