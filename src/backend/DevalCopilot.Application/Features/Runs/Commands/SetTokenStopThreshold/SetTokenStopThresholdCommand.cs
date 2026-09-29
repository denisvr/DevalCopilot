using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.SetTokenStopThreshold;

/// <summary>
/// Sets or clears one provider's optional token-activity stop threshold for this run.
/// <see cref="Provider"/> is exactly <c>"Codex"</c> or <c>"ClaudeCode"</c>; the other provider's
/// threshold is never touched. <see cref="ThresholdTokens"/> <see langword="null"/> clears; a
/// non-null value must be positive and within <c>Run.MaxTokenStopThreshold</c>. Once the provider's
/// locally recorded token activity reaches the threshold, the next Agent claim for that provider is
/// refused. It applies to future claims only, and is not an account allowance or a per-attempt cap.
///
/// <para>
/// Manual transaction: the handler's single <c>SaveChangesAsync</c> must own the implicit database
/// transaction that commits the Run change and its event together. Under the mediator's ambient
/// transaction a failed concurrency-checked UPDATE could leave the already-executed event INSERT in
/// that outer transaction.
/// </para>
/// </summary>
public sealed record SetTokenStopThresholdCommand(Guid RunId, string Provider, long? ThresholdTokens)
    : IManualTransactionCommand<Result<SetTokenStopThresholdCommandResult>>;
