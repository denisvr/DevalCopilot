using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.EnvironmentReadiness.Commands.MarkHostCapabilityProbeDispatched;

public sealed class MarkHostCapabilityProbeDispatchedCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<MarkHostCapabilityProbeDispatchedCommand, Result<DateTimeOffset>>
{
    public async Task<Result<DateTimeOffset>> HandleAsync(
        MarkHostCapabilityProbeDispatchedCommand command, CancellationToken cancellationToken)
    {
        var snapshot = await dbContext.HostCapabilitySnapshots
            .SingleOrDefaultAsync(candidate => candidate.Capability == command.Capability, cancellationToken);

        if (snapshot is null)
        {
            return Result<DateTimeOffset>.Failure(
                Error.NotFound("host_capabilities.not_found", "The requested capability snapshot was not found."));
        }

        if (snapshot.ProbeDispatchedAtUtc.HasValue)
        {
            return Result<DateTimeOffset>.Failure(
                Error.Conflict("host_capabilities.already_dispatched", "The capability probe was already dispatched."));
        }

        var nowUtc = timeProvider.GetUtcNow();
        snapshot.MarkDispatched(nowUtc);

        return Result<DateTimeOffset>.Success(nowUtc);
    }
}
