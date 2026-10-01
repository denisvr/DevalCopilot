using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.StartSimulatedRun;

/// <summary>Owns its save so a serialization loss can be reported as a conflict.</summary>
public sealed record StartSimulatedRunCommand(Guid ProjectId, string Objective)
    : IManualTransactionCommand<Result<StartSimulatedRunCommandResult>>;
