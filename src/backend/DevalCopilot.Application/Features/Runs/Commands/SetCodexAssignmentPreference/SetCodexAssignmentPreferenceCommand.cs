using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.SetCodexAssignmentPreference;

/// <summary>
/// Sets or clears the owner's explicit, run-scoped requested Codex model and optional reasoning
/// effort for this run's later Planner, Challenge Resolver, and Code Reviewer claims.
/// <see cref="RequestedModel"/> <see langword="null"/> clears any current preference without a
/// provider read; a non-null value is validated against one fresh, bounded catalog observation
/// before it is durably recorded. This is never selected or effective configuration, an
/// invocation-eligibility guarantee, or a change to any already-claimed attempt's own immutable
/// assignment.
///
/// <para>
/// Manual transaction: a non-null preference performs a bounded external Codex App Server
/// process invocation to validate it, which must never run inside the mediator's ambient EF
/// transaction — that would hold a database connection and transaction open for the entire
/// external process round trip. The handler performs that observation outside any transaction,
/// then re-reads the Run and commits its preference and change event together in one short,
/// tightly scoped write.
/// </para>
/// </summary>
public sealed record SetCodexAssignmentPreferenceCommand(
    Guid RunId,
    string? RequestedModel,
    string? RequestedEffort) : IManualTransactionCommand<Result<SetCodexAssignmentPreferenceCommandResult>>;
