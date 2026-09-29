using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.SetClaudeModelPreference;

/// <summary>
/// Sets or clears the owner's explicit, run-scoped requested Claude model alias for this run's later
/// CriticalReviewer, Implementer, and ReviewCorrection claims, together with an optional effort
/// level saved as one pair. <see cref="RequestedModel"/>
/// <see langword="null"/> clears the preference (no <c>--model</c> override); a non-null value must
/// be one of the closed <c>ClaudeModelAlias</c> members, and <see cref="RequestedEffort"/> (a closed
/// <c>ClaudeEffortLevel</c>) is accepted only with an explicit <c>sonnet</c> or <c>opus</c> alias. This is a request only: never an observed
/// or effective model, an account-eligibility guarantee, or a change to any already-claimed
/// attempt's own immutable snapshot.
///
/// <para>
/// Manual transaction: the handler's single <c>SaveChangesAsync</c> must own the implicit
/// database transaction that commits the Run change and its event together. Under the mediator's
/// ambient transaction a failed concurrency-checked UPDATE could leave the already-executed event
/// INSERT in that outer transaction.
/// </para>
/// </summary>
public sealed record SetClaudeModelPreferenceCommand(Guid RunId, string? RequestedModel, string? RequestedEffort = null)
    : IManualTransactionCommand<Result<SetClaudeModelPreferenceCommandResult>>;
