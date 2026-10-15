using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.GitHubObservation;

namespace DevalCopilot.Application.Features.Runs.Policies.GitHubBranchPublicationApproval;

/// <summary>
/// Reads the complete scope of a publication approval through the unchanged ADR-0040 source and target readers and the ADR-0043 binding
/// reader, each fresh and untracked over pinned rows (ADR-0048). Nothing is looked up as "latest" or "current", and no origin, remote, Git,
/// filesystem or process is touched. The binding reader receives the coherent target only, so a binding without one is an orphan.
/// </summary>
internal static class GitHubBranchPublicationScopeReader
{
    public static async Task<GitHubBranchPublicationScopeReading> ReadAsync(
        IDevalCopilotDbContext dbContext, Guid runId, CancellationToken cancellationToken)
    {
        var source = await GitHubObservationSourceReader.ReadAsync(dbContext, runId, cancellationToken);
        var target = await GitHubObservationTargetReader.ReadAsync(dbContext, runId, source, cancellationToken);
        var coherentTarget = target.Kind == GitHubObservationTargetReadingKind.Coherent ? target.Target : null;
        var binding = await GitHubRepositoryIdentityBindingReader.ReadAsync(dbContext, runId, source, coherentTarget, cancellationToken);
        return new GitHubBranchPublicationScopeReading(source, target, binding);
    }
}
