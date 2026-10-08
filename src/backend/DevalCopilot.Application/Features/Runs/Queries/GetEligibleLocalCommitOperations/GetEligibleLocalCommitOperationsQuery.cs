using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetEligibleLocalCommitOperations;

/// <summary>The identifiers of admitted local-commit operations still awaiting their single execution. It only proposes
/// candidates: the execution command re-reads and decides again.</summary>
public sealed record GetEligibleLocalCommitOperationsQuery : IQuery<Result<IReadOnlyList<Guid>>>;
