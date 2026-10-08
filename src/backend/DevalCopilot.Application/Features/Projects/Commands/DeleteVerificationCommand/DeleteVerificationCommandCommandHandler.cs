using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Policies;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Commands.DeleteVerificationCommand;

public sealed class DeleteVerificationCommandCommandHandler(IDevalCopilotDbContext dbContext)
    : ICommandHandler<DeleteVerificationCommandCommand, Result>
{
    public async Task<Result> HandleAsync(DeleteVerificationCommandCommand command, CancellationToken cancellationToken)
    {
        var verificationCommand = await dbContext.VerificationCommands.SingleOrDefaultAsync(
            configured => configured.Id == command.VerificationCommandId && configured.ProjectId == command.ProjectId,
            cancellationToken);
        if (verificationCommand is null)
        {
            return Result.Failure(Error.NotFound("verification_commands.not_found", "This verification command does not exist."));
        }

        if (await CommitReservation.IsProjectReservedAsync(dbContext, command.ProjectId, cancellationToken))
        {
            return Result.Failure(CommitReservation.WorkspaceCommitting());
        }

        dbContext.VerificationCommands.Remove(verificationCommand);
        return await CommitReservation.TrySaveAsync(dbContext, command.ProjectId, cancellationToken) is { } conflict
            ? Result.Failure(conflict)
            : Result.Success();
    }
}
