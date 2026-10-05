using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.CreateManualRun;

/// <summary>
/// Records a user-written objective as a durable manual Agent run and nothing else: no workspace
/// preparation, checkpoint, readiness probe, manifest, attempt, provider invocation, lease, or Agent
/// budget consumption. Owns its save so a serialization loss can be reported as a conflict.
/// The two optional members choose smaller immutable run-wide ceilings (ADR-0028); each independently
/// defaults when <see langword="null"/>.
/// </summary>
public sealed record CreateManualRunCommand(
    Guid ProjectId,
    string Objective,
    int? MaximumAgentAttempts = null,
    int? MaximumAgentInvocationMinutes = null)
    : IManualTransactionCommand<Result<CreateManualRunCommandResult>>;
