using System.Text.Json;
using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Commands.StartSimulatedRun;

public sealed class StartSimulatedRunCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<StartSimulatedRunCommand, Result<StartSimulatedRunCommandResult>>
{
    public async Task<Result<StartSimulatedRunCommandResult>> HandleAsync(
        StartSimulatedRunCommand command,
        CancellationToken cancellationToken)
    {
        var project = await dbContext.Projects
            .SingleOrDefaultAsync(candidate => candidate.Id == command.ProjectId, cancellationToken);

        if (project is null)
        {
            return Result<StartSimulatedRunCommandResult>.Failure(
                Error.NotFound("projects.not_found", "The requested project was not found."));
        }

        var nowUtc = timeProvider.GetUtcNow();
        var executionNumber = project.ReserveExecutionNumber();
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, executionNumber, command.Objective, nowUtc);
        dbContext.Runs.Add(run);

        var payload = JsonSerializer.Serialize(new { objective = command.Objective });
        dbContext.Events.Add(
            RunEvent.Record(Guid.NewGuid(), run.Id, attemptId: null, RunEventType.RunStarted, ParticipantIdentity.ForOrchestrator(), payload, nowUtc));

        return Result<StartSimulatedRunCommandResult>.Success(
            new StartSimulatedRunCommandResult(run.Id, run.ExecutionNumber));
    }
}
