using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetHostCapabilityReadiness;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectGitHubOrigin;

/// <summary>
/// Reads the project and the shared Git discovery snapshot untracked and invokes the observer only for a current,
/// successful, coherent direct-executable Git target and a stored, resolved physical identity that the current root still
/// matches both before and after the observation. Anything else is <see cref="GitHubOriginStatus.Unavailable"/> with
/// nothing invoked (or the observation discarded); this query never discovers, rechecks, backfills, updates an identity,
/// leases, claims or writes. Only an unknown project is an expected failure; every unexpected fault propagates.
/// </summary>
public sealed class GetProjectGitHubOriginQueryHandler(
    IDevalCopilotDbContext dbContext,
    IRepositoryRootPathInspector rootPathInspector,
    IRepositoryPhysicalIdentityInspector physicalIdentityInspector,
    IGitHubOriginObserver observer,
    TimeProvider timeProvider)
    : IQueryHandler<GetProjectGitHubOriginQuery, Result<GetProjectGitHubOriginQueryResult>>
{
    public async Task<Result<GetProjectGitHubOriginQueryResult>> HandleAsync(
        GetProjectGitHubOriginQuery query, CancellationToken cancellationToken)
    {
        var project = await dbContext.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == query.ProjectId, cancellationToken);
        if (project is null)
        {
            return Result<GetProjectGitHubOriginQueryResult>.Failure(
                Error.NotFound("projects.not_found", "This project does not exist."));
        }

        var unavailable = Result<GetProjectGitHubOriginQueryResult>.Success(
            GetProjectGitHubOriginQueryResult.Unavailable(project.Id));

        var snapshot = await dbContext.HostCapabilitySnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Capability == Capability.Git, cancellationToken);
        if (!IsUsableTarget(snapshot, timeProvider.GetUtcNow()) || !HasStoredIdentity(project))
        {
            return unavailable;
        }

        var candidate = CurrentCandidate(project);
        if (candidate is null)
        {
            return unavailable;
        }

        var observation = await observer.ObserveAsync(snapshot!.ResolvedExecutablePath!, candidate, cancellationToken);

        var after = CurrentCandidate(project);
        if (after is null || !string.Equals(after.CanonicalPath, candidate.CanonicalPath, StringComparison.Ordinal))
        {
            return unavailable;
        }

        return Result<GetProjectGitHubOriginQueryResult>.Success(Normalize(project.Id, observation, timeProvider.GetUtcNow()));
    }

    private static bool IsUsableTarget(HostCapabilitySnapshot? snapshot, DateTimeOffset nowUtc) =>
        snapshot is not null
        && snapshot.ReasonCode == CapabilityProbeReason.None
        && snapshot.LaunchKind == CapabilityLaunchKind.DirectExecutable
        && snapshot.ResolvedScriptPath is null
        && !string.IsNullOrWhiteSpace(snapshot.ResolvedExecutablePath)
        && Path.IsPathFullyQualified(snapshot.ResolvedExecutablePath)
        && !string.IsNullOrWhiteSpace(snapshot.ObservedVersion)
        && snapshot.EvidenceObservedAtUtc.HasValue
        && !HostCapabilityReadinessProjector.IsStale(snapshot, nowUtc);

    private static bool HasStoredIdentity(Project project) =>
        project.PhysicalIdentityStatus == PhysicalIdentityStatus.Resolved
        && project.PhysicalVolumeSerialNumber.HasValue
        && project.PhysicalFileId is { Length: 16 };

    /// <summary>
    /// The root as the existing read-only ports see it right now, only while its physical identity still equals the stored
    /// tuple. Nothing is persisted, recorded or updated either way.
    /// </summary>
    private RepositoryRootCandidate? CurrentCandidate(Project project)
    {
        var root = rootPathInspector.Inspect(project.CanonicalPath);
        if (root.Outcome != RepositoryRootInspectionOutcome.Success || root.Candidate is null)
        {
            return null;
        }

        var identity = physicalIdentityInspector.Resolve(root.Candidate);

        return identity.Outcome == RepositoryPhysicalIdentityInspectionOutcome.Resolved
            && identity.VolumeSerialNumber.HasValue
            && identity.FileId is { Length: 16 }
            && project.PhysicalIdentityMatches(identity.VolumeSerialNumber.Value, identity.FileId)
                ? root.Candidate
                : null;
    }

    /// <summary>An observer can report only a coherent observation: a target exactly for Available, a time for both observed statuses.</summary>
    private static GetProjectGitHubOriginQueryResult Normalize(Guid projectId, GitHubOriginObservation observation, DateTimeOffset nowUtc) =>
        observation switch
        {
            { Status: GitHubOriginStatus.NotConfigured, Target: null } =>
                new(projectId, GitHubOriginStatus.NotConfigured, nowUtc, null),
            { Status: GitHubOriginStatus.Available, Target: { Owner.Length: > 0, Repository.Length: > 0 } target } =>
                new(projectId, GitHubOriginStatus.Available, nowUtc, target),
            _ => GetProjectGitHubOriginQueryResult.Unavailable(projectId),
        };
}
