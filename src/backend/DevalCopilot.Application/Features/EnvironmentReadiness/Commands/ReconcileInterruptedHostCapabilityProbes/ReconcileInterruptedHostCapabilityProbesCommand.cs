using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.EnvironmentReadiness.Commands.ReconcileInterruptedHostCapabilityProbes;

/// <summary>
/// Startup reconciliation: clears any capability's dispatch marker left stuck by a crash or a
/// stalled recording, making it immediately eligible again. Never invents a terminal failure —
/// a version probe is safely repeatable, so this simply retries rather than escalating.
/// </summary>
public sealed record ReconcileInterruptedHostCapabilityProbesCommand : ICommand<Result<int>>;
