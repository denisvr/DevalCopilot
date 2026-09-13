using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedProcessAttempts;

/// <summary>
/// Startup-only reconciliation: every Process attempt an application restart found still
/// <see cref="Domain.Features.Runs.AttemptStatus.Running"/> — with no owning process left to
/// observe it — becomes <see cref="Domain.Features.Runs.AttemptStatus.Interrupted"/>, together
/// with its run. Never touches the Simulated flow.
/// </summary>
public sealed record ReconcileInterruptedProcessAttemptsCommand : ICommand<Result<int>>;
