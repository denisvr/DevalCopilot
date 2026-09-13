using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.EnvironmentReadiness.Commands.RequestHostCapabilityRefresh;

public sealed class RequestHostCapabilityRefreshCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<RequestHostCapabilityRefreshCommand, Result<DateTimeOffset>>
{
    public async Task<Result<DateTimeOffset>> HandleAsync(
        RequestHostCapabilityRefreshCommand command, CancellationToken cancellationToken)
    {
        var snapshot = await dbContext.HostCapabilitySnapshots
            .SingleOrDefaultAsync(candidate => candidate.Capability == command.Capability, cancellationToken);

        if (snapshot is null)
        {
            return Result<DateTimeOffset>.Failure(
                Error.NotFound("host_capabilities.not_found", "The requested capability snapshot was not found."));
        }

        var nowUtc = timeProvider.GetUtcNow();
        snapshot.PullProbeDueForward(nowUtc);

        return Result<DateTimeOffset>.Success(snapshot.NextProbeDueAtUtc);
    }
}
