using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetManualRunAbandonment;

/// <summary>Read-only abandonment eligibility and recorded abandonment of a run (ADR-0031). It writes nothing and invokes no provider; an
/// eligible answer is advisory, because the command decides again from fresh authority under its own write lock.</summary>
public sealed record GetManualRunAbandonmentQuery(Guid RunId) : IQuery<Result<GetManualRunAbandonmentQueryResult>>;
