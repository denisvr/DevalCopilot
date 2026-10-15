using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubBranchPublicationApproval;

/// <summary>Read-only state of a run's recorded GitHub branch publication approval (ADR-0048). It reads persisted rows only through untracked
/// projections: no Git, filesystem, process, provider or network, and it writes, locks and repairs nothing. A recorded approval is
/// historical consent, never readiness, a permission or a push.</summary>
public sealed record GetGitHubBranchPublicationApprovalQuery(Guid RunId)
    : IQuery<Result<GetGitHubBranchPublicationApprovalQueryResult>>;
