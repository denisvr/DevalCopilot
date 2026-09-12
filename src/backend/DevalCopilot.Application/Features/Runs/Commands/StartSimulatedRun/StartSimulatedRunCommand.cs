using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Commands.StartSimulatedRun;

public sealed record StartSimulatedRunCommand(Guid ProjectId, string Objective)
    : ICommand<Result<StartSimulatedRunCommandResult>>;
