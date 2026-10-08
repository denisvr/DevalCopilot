using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Policies;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Commands.ConfigureVerificationCommand;

public sealed class ConfigureVerificationCommandCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<ConfigureVerificationCommandCommand, Result<ConfigureVerificationCommandCommandResult>>
{
    public async Task<Result<ConfigureVerificationCommandCommandResult>> HandleAsync(
        ConfigureVerificationCommandCommand command, CancellationToken cancellationToken)
    {
        var project = await dbContext.Projects.SingleOrDefaultAsync(project => project.Id == command.ProjectId, cancellationToken);
        if (project is null)
        {
            return Result<ConfigureVerificationCommandCommandResult>.Failure(
                Error.NotFound("projects.not_found", "This project does not exist."));
        }

        if (await CommitReservation.IsProjectReservedAsync(dbContext, project.Id, cancellationToken))
        {
            return Result<ConfigureVerificationCommandCommandResult>.Failure(CommitReservation.WorkspaceCommitting());
        }

        var nowUtc = timeProvider.GetUtcNow();
        var verificationCommand = VerificationCommand.Configure(
            Guid.NewGuid(),
            project.Id,
            project.ReserveVerificationCommandNumber(),
            command.Name,
            command.ExecutablePath,
            command.Arguments,
            command.TimeoutSeconds,
            command.IsEnabled,
            nowUtc);
        dbContext.VerificationCommands.Add(verificationCommand);
        if (await CommitReservation.TrySaveAsync(dbContext, project.Id, cancellationToken) is { } conflict)
        {
            return Result<ConfigureVerificationCommandCommandResult>.Failure(conflict);
        }

        return Result<ConfigureVerificationCommandCommandResult>.Success(
            new ConfigureVerificationCommandCommandResult(verificationCommand.Id, verificationCommand.CommandNumber));
    }
}
