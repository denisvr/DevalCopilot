using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.EnvironmentReadiness.Commands.RecordHostCapabilityProbeResult;

public sealed class RecordHostCapabilityProbeResultCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<RecordHostCapabilityProbeResultCommand, Result<CapabilityProbeReason>>
{
    /// <summary>How long a successfully or unsuccessfully recorded probe stays due before the
    /// next one — the recurring poll cadence for this capability, not a bound on any single
    /// probe's own execution time.</summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    public async Task<Result<CapabilityProbeReason>> HandleAsync(
        RecordHostCapabilityProbeResultCommand command, CancellationToken cancellationToken)
    {
        var snapshot = await dbContext.HostCapabilitySnapshots
            .SingleOrDefaultAsync(candidate => candidate.Capability == command.Capability, cancellationToken);

        if (snapshot is null)
        {
            return Result<CapabilityProbeReason>.Failure(
                Error.NotFound("host_capabilities.not_found", "The requested capability snapshot was not found."));
        }

        if (!snapshot.ProbeDispatchedAtUtc.HasValue)
        {
            return Result<CapabilityProbeReason>.Failure(
                Error.Conflict("host_capabilities.not_dispatched", "The capability probe was not dispatched."));
        }

        var nowUtc = timeProvider.GetUtcNow();
        var nextProbeDueAtUtc = nowUtc + RefreshInterval;

        if (command.Outcome.Reason == CapabilityProbeReason.None)
        {
            snapshot.RecordSuccess(
                command.Outcome.ResolvedExecutablePath!, command.Outcome.Version!, nowUtc, nextProbeDueAtUtc);
        }
        else
        {
            snapshot.RecordFailure(command.Outcome.Reason, nextProbeDueAtUtc);
        }

        return Result<CapabilityProbeReason>.Success(snapshot.ReasonCode);
    }
}
