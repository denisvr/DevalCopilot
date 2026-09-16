using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Commands.ClaimVerificationExecution;

public sealed class ClaimVerificationExecutionCommandHandler(
    IDevalCopilotDbContext dbContext,
    IGitWorkspaceEvidenceReader evidenceReader,
    TimeProvider timeProvider)
    : ICommandHandler<ClaimVerificationExecutionCommand, Result<ClaimVerificationExecutionCommandResult>>
{
    public async Task<Result<ClaimVerificationExecutionCommandResult>> HandleAsync(
        ClaimVerificationExecutionCommand command, CancellationToken cancellationToken)
    {
        var project = await dbContext.Projects.SingleOrDefaultAsync(candidate => candidate.Id == command.ProjectId, cancellationToken);
        var recipe = await dbContext.VerificationCommands.SingleOrDefaultAsync(
            candidate => candidate.Id == command.VerificationCommandId && candidate.ProjectId == command.ProjectId,
            cancellationToken);
        var checkpoint = await dbContext.GitCheckpoints.SingleOrDefaultAsync(candidate => candidate.Id == command.GitCheckpointId, cancellationToken);
        var workspace = await dbContext.GitWorkspaces
            .Where(candidate => candidate.ProjectId == command.ProjectId)
            .OrderByDescending(candidate => candidate.WorkspaceNumber)
            .FirstOrDefaultAsync(cancellationToken);

        if (project is null || recipe is null || checkpoint is null || workspace is null || checkpoint.WorkspaceId != workspace.Id)
        {
            return Result<ClaimVerificationExecutionCommandResult>.Failure(
                Error.NotFound("verification.not_found", "The verification recipe or source checkpoint was not found for this project."));
        }

        if (!recipe.IsEnabled)
        {
            return Result<ClaimVerificationExecutionCommandResult>.Failure(
                Error.Conflict("verification.disabled", "This verification recipe is disabled."));
        }

        if (workspace.Status != WorkspaceStatus.Ready || !await dbContext.RepositoryMutationLeases.AnyAsync(
                lease => lease.WorkspaceId == workspace.Id && lease.Status == LeaseStatus.Active, cancellationToken))
        {
            return Result<ClaimVerificationExecutionCommandResult>.Failure(
                Error.Conflict("verification.workspace_not_ready", "A ready, owned workspace is required to run verification."));
        }

        if (await dbContext.VerificationExecutions.AnyAsync(
                execution => execution.GitWorkspaceId == workspace.Id && execution.Status == VerificationExecutionStatus.Running,
                cancellationToken))
        {
            return Result<ClaimVerificationExecutionCommandResult>.Failure(
                Error.Conflict("verification.already_running", "This workspace already has a verification execution in progress."));
        }

        var evidence = await evidenceReader.CaptureAsync(workspace.WorkspacePath, cancellationToken);
        if (evidence.Outcome != GitWorkspaceEvidenceOutcome.Success || evidence.FingerprintSha256 != checkpoint.FingerprintSha256)
        {
            return Result<ClaimVerificationExecutionCommandResult>.Failure(
                Error.Conflict("verification.checkpoint_not_current", "The selected source checkpoint is no longer current for this workspace."));
        }

        var execution = VerificationExecution.Claim(
            Guid.NewGuid(), project.Id, project.ReserveVerificationExecutionNumber(), workspace, checkpoint, recipe, timeProvider.GetUtcNow());
        dbContext.VerificationExecutions.Add(execution);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<ClaimVerificationExecutionCommandResult>.Success(new(execution.Id, execution.ExecutionNumber));
    }
}
