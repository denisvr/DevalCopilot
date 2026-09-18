using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedAgentAttempts;

/// <summary>
/// Marks every Agent attempt this host instance finds still <c>Running</c> at startup as
/// <c>Interrupted</c>, and its owning run alongside it — the previous host stopped without a
/// terminal transition, so neither can be trusted to still be in flight. Must run after startup
/// artifact recovery has already imported whatever sealed/partial evidence exists for these same
/// attempts, and before the Agent attempt supervisor starts polling.
/// </summary>
public sealed record ReconcileInterruptedAgentAttemptsCommand : ICommand<Result<int>>;
