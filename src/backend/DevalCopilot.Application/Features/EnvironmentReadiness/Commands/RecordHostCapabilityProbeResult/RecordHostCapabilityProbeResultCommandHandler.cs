using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.EnvironmentReadiness.Ports;
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
            // Defense in depth: ToolDiscoveryResult's own factories already make an invalid
            // success combination impossible to construct, but this handler never trusts that
            // guarantee alone — it fails safely with a stable, project-owned error rather than
            // risking a NullReferenceException/InvalidOperationException/ArgumentOutOfRangeException
            // from an adapter result that reached here some other way, and it never partially
            // mutates the snapshot first. Rejects an undefined launch kind and a relative
            // executable/script path here too — not just blank ones — since HostCapabilitySnapshot
            // is the durable owner of these paths and this is the boundary in front of it.
            if (command.Outcome.LaunchKind is not { } launchKind ||
                !Enum.IsDefined(launchKind) ||
                string.IsNullOrWhiteSpace(command.Outcome.ResolvedExecutablePath) ||
                !Path.IsPathFullyQualified(command.Outcome.ResolvedExecutablePath) ||
                string.IsNullOrWhiteSpace(command.Outcome.Version) ||
                (launchKind == CapabilityLaunchKind.NodeScript &&
                    (string.IsNullOrWhiteSpace(command.Outcome.ResolvedScriptPath) ||
                     !Path.IsPathFullyQualified(command.Outcome.ResolvedScriptPath))) ||
                (launchKind == CapabilityLaunchKind.DirectExecutable && command.Outcome.ResolvedScriptPath is not null))
            {
                return InvalidProbeResult();
            }

            snapshot.RecordSuccess(
                launchKind,
                command.Outcome.ResolvedExecutablePath,
                command.Outcome.ResolvedScriptPath,
                command.Outcome.Version,
                nowUtc,
                nextProbeDueAtUtc);
        }
        else if (ToolDiscoveryResult.IsValidFailureReason(command.Outcome.Reason))
        {
            snapshot.RecordFailure(command.Outcome.Reason, nextProbeDueAtUtc);
        }
        else
        {
            return InvalidProbeResult();
        }

        return Result<CapabilityProbeReason>.Success(snapshot.ReasonCode);
    }

    private static Result<CapabilityProbeReason> InvalidProbeResult() =>
        Result<CapabilityProbeReason>.Failure(
            Error.Conflict("host_capabilities.invalid_probe_result", "The capability probe result was invalid."));
}
