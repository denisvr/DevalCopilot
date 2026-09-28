using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.SetTokenWarningThreshold;

/// <summary>
/// Sets or clears one provider's optional advisory token-activity warning threshold for this run.
/// <see cref="Provider"/> is exactly <c>"Codex"</c> or <c>"ClaudeCode"</c>; the other provider's
/// threshold is never touched. <see cref="ThresholdTokens"/> <see langword="null"/> clears; a
/// non-null value must be positive and within <c>Run.MaxTokenWarningThreshold</c>. The threshold is
/// advisory only: never a budget, and never read by any claim, dispatch, or adapter path.
///
/// <para>
/// Manual transaction: the handler's single <c>SaveChangesAsync</c> must own the implicit database
/// transaction that commits the Run change and its event together. Under the mediator's ambient
/// transaction a failed concurrency-checked UPDATE could leave the already-executed event INSERT in
/// that outer transaction.
/// </para>
/// </summary>
public sealed record SetTokenWarningThresholdCommand(Guid RunId, string Provider, long? ThresholdTokens)
    : IManualTransactionCommand<Result<SetTokenWarningThresholdCommandResult>>;
