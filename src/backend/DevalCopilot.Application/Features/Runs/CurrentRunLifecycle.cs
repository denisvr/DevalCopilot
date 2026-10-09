using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs;

/// <summary>
/// The fresh lifecycle confirmation for the Codex claim paths (ADR-0031). A Claude claim commits through a Run UPDATE whose
/// <c>Lifecycle</c> concurrency token already makes it lose to any committed transition, and a Codex claim of a Created run does the
/// same through <c>Run.Claim</c>. A Codex claim of a Running run changes no Run column, so none of its compare-and-update guards
/// observed the lifecycle: an explicit abandonment that committed after the claim decided would not have stopped it. This read closes
/// that gap. It must be called inside the claim's short transaction AFTER its first guard write has taken the database write lock, so
/// the answer is atomic with the Attempt insert that follows and no abandonment can commit between them.
/// </summary>
public static class CurrentRunLifecycle
{
    public const string NotActiveCode = "runs.not_active";

    /// <summary>True only while the run is Created (when the claim path admits it) or Running, read afresh and untracked.</summary>
    public static async Task<bool> IsStillActiveAsync(
        IDevalCopilotDbContext dbContext, Guid runId, bool allowCreated, CancellationToken cancellationToken) =>
        await dbContext.Runs.AsNoTracking().AnyAsync(
            candidate => candidate.Id == runId
                && (candidate.Lifecycle == RunLifecycle.Running || (allowCreated && candidate.Lifecycle == RunLifecycle.Created)),
            cancellationToken);

    /// <summary>The refusal code the challenge-resolution and code-review claims already used for a run that is not Running, kept so the
    /// code of an ended run is the same whether it is seen early or at the claim seam.</summary>
    public const string NotRunningCode = "runs.not_running";

    public static Error NotActive() => Error.Conflict(
        NotActiveCode, "The run ended while this attempt was being claimed and can no longer start it.");

    public static Error NotRunning() => Error.Conflict(
        NotRunningCode, "The run ended while this attempt was being claimed and can no longer start it.");
}
