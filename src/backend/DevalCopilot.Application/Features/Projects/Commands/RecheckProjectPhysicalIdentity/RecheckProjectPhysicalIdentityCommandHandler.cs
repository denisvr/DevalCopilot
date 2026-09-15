using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Projects.Ports;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Commands.RecheckProjectPhysicalIdentity;

/// <summary>
/// The dedicated, explicitly user-triggered recovery action for a project whose physical
/// identity is <c>Unresolved</c> or <c>Unavailable</c> — exactly one bounded resolution attempt
/// per invocation, never automatic, scheduled, or retried in a loop. Shares its resolution
/// semantics with <c>PrepareRepositoryWorkspaceCommandHandler</c>'s own inline recheck via
/// <see cref="PhysicalIdentityRecheck"/>, never a second, divergent implementation. See ADR-0008.
/// </summary>
public sealed class RecheckProjectPhysicalIdentityCommandHandler(
    IDevalCopilotDbContext dbContext,
    IRepositoryRootPathInspector rootPathInspector,
    IRepositoryPhysicalIdentityInspector physicalIdentityInspector)
    : ICommandHandler<RecheckProjectPhysicalIdentityCommand, Result<RecheckProjectPhysicalIdentityCommandResult>>
{
    public async Task<Result<RecheckProjectPhysicalIdentityCommandResult>> HandleAsync(
        RecheckProjectPhysicalIdentityCommand command, CancellationToken cancellationToken)
    {
        var project = await dbContext.Projects.SingleOrDefaultAsync(p => p.Id == command.ProjectId, cancellationToken);
        if (project is null)
        {
            return Result<RecheckProjectPhysicalIdentityCommandResult>.Failure(
                Error.NotFound("projects.not_found", "This project does not exist."));
        }

        var outcome = PhysicalIdentityRecheck.Apply(project, rootPathInspector, physicalIdentityInspector);

        return outcome.IsFailure
            ? Result<RecheckProjectPhysicalIdentityCommandResult>.Failure(outcome.Errors[0])
            : Result<RecheckProjectPhysicalIdentityCommandResult>.Success(
                new RecheckProjectPhysicalIdentityCommandResult(outcome.Value));
    }
}
