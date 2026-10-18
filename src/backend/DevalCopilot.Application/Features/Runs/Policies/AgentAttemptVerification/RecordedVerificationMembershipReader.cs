using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;

/// <summary>
/// Proves the whole recorded verification membership of one CodeReviewer attempt from bounded scalar reads (ADR-0052), for both the
/// metadata inspection and every sealed window, which deliberately change together. It applies the unchanged scalar identity evaluation
/// (ADR-0037/0051), reads at most 33 memberships in stored order through the attempt/sequence index, judges at most 32, and proves the
/// exact historical ownership of the attempt's recorded workspace and checkpoint and of every referenced execution before any member is
/// returned. No current recipe, latest execution, current checkpoint, active workspace, lease, eligibility or authority reader is ever
/// consulted, and no membership digest is read. It writes nothing and swallows no fault.
/// </summary>
internal static class RecordedVerificationMembershipReader
{
    public const int MaximumInspectedMembers = RecordedVerificationProjection.PositionBound;

    private const int FingerprintLength = 64;
    private const int MaximumCommandNameLength = 200;

    public static async Task<RecordedVerificationMembershipReading> ReadAsync(
        IDevalCopilotDbContext dbContext, Guid runId, Guid attemptId, CancellationToken cancellationToken)
    {
        // Exactly this attempt of this run; an unknown run, a missing or foreign attempt and a non-Agent attempt are the same answer.
        var attempt = await dbContext.Attempts.AsNoTracking()
            .Where(candidate => candidate.RunId == runId && candidate.Id == attemptId)
            .ProjectScalarIdentity()
            .FirstOrDefaultAsync(cancellationToken);
        if (attempt is null || !attempt.IsAgent)
        {
            return RecordedVerificationMembershipReading.NotFound;
        }

        var identity = ScalarAgentAttemptIdentity.Evaluate(attempt);
        if (identity is not { Role: AgentRole.CodeReviewer }
            || identity.ResponseContract is not (AgentResponseContract.ImplementationReview or AgentResponseContract.VerificationDiagnosis))
        {
            return RecordedVerificationMembershipReading.Unavailable;
        }

        var ownership = await dbContext.Attempts.AsNoTracking()
            .Where(candidate => candidate.RunId == runId && candidate.Id == attemptId)
            .ProjectVerificationOwnership(dbContext.Runs.AsNoTracking())
            .FirstOrDefaultAsync(cancellationToken);
        if (ownership is not { WorkspaceId: { } workspaceId, CheckpointId: { } checkpointId, FingerprintOversized: false }
            || workspaceId == Guid.Empty
            || checkpointId == Guid.Empty
            || !IsFingerprint(ownership.Fingerprint))
        {
            return RecordedVerificationMembershipReading.Unavailable;
        }

        // The recorded checkpoint by identity, never the workspace's latest: it belongs to the recorded workspace, that workspace to the
        // run's project, and its fingerprint is the one the attempt recorded.
        var checkpoint = await dbContext.GitCheckpoints.AsNoTracking()
            .Where(candidate => candidate.Id == checkpointId)
            .ProjectCheckpointOwnership(dbContext.GitWorkspaces.AsNoTracking())
            .FirstOrDefaultAsync(cancellationToken);
        if (checkpoint is not { Number: { } checkpointNumber, FingerprintOversized: false }
            || checkpoint.WorkspaceId != workspaceId
            || checkpoint.WorkspaceProjectId != ownership.ProjectId
            || !string.Equals(checkpoint.Fingerprint, ownership.Fingerprint, StringComparison.Ordinal))
        {
            return RecordedVerificationMembershipReading.Unavailable;
        }

        // One more row than can be judged, in stored order: observing it is enough to refuse, without a count or a partial list.
        var memberships = await dbContext.AttemptVerificationEvidence.AsNoTracking()
            .Where(membership => membership.AttemptId == attemptId)
            .OrderBy(membership => membership.Sequence)
            .Take(MaximumInspectedMembers + 1)
            .ProjectMembership()
            .ToArrayAsync(cancellationToken);
        if (memberships.Length is 0 or > MaximumInspectedMembers)
        {
            return RecordedVerificationMembershipReading.Unavailable;
        }

        var commandIds = new HashSet<Guid>();
        var executionIds = new HashSet<Guid>();
        for (var index = 0; index < memberships.Length; index++)
        {
            var membership = memberships[index];
            if (membership.Position != index
                || membership.CommandId == Guid.Empty
                || membership.ExecutionId == Guid.Empty
                || !commandIds.Add(membership.CommandId)
                || !executionIds.Add(membership.ExecutionId))
            {
                return RecordedVerificationMembershipReading.Unavailable;
            }
        }

        // Exactly the recorded executions by primary key (at most 32 rows): a missing one is never omitted or replaced.
        var referencedIds = executionIds.ToArray();
        var executions = await dbContext.VerificationExecutions.AsNoTracking()
            .Where(execution => referencedIds.Contains(execution.Id))
            .ProjectExecution()
            .ToArrayAsync(cancellationToken);
        var executionsById = executions.ToDictionary(execution => execution.Id);

        var diagnosis = identity.ResponseContract == AgentResponseContract.VerificationDiagnosis;
        var members = new RecordedVerificationMember[memberships.Length];
        for (var index = 0; index < memberships.Length; index++)
        {
            var membership = memberships[index];
            if (!executionsById.TryGetValue(membership.ExecutionId, out var execution)
                || execution.CommandId != membership.CommandId
                || execution.ProjectId != ownership.ProjectId
                || execution.WorkspaceId != workspaceId
                || execution.CheckpointId != checkpointId
                || !string.Equals(execution.ClaimFingerprint, ownership.Fingerprint, StringComparison.Ordinal)
                || !string.Equals(execution.CompletionFingerprint, ownership.Fingerprint, StringComparison.Ordinal)
                || execution.ClaimFingerprintOversized
                || execution.CompletionFingerprintOversized
                || execution is not { Number: { } executionNumber, DispatchedAtUtc: { } dispatchedAtUtc, CompletedAtUtc: { } completedAtUtc }
                || !IsCommandName(execution)
                || !TryClassifyTerminal(execution, diagnosis, out var failed, out var exitCode))
            {
                return RecordedVerificationMembershipReading.Unavailable;
            }

            members[index] = new RecordedVerificationMember(
                index,
                membership.CommandId,
                membership.ExecutionId,
                executionNumber,
                execution.CommandName!,
                failed,
                exitCode,
                execution.ClaimedAtUtc,
                dispatchedAtUtc,
                completedAtUtc);
        }

        // A diagnosis is claimed only against at least one failed verification; a set without one is not a diagnosis membership.
        if (diagnosis && !members.Any(member => member.Failed))
        {
            return RecordedVerificationMembershipReading.Unavailable;
        }

        return RecordedVerificationMembershipReading.Proven(new RecordedVerificationMembership(
            attempt.Id, attempt.AttemptNumber, identity, checkpointId, checkpointNumber, members));
    }

    /// <summary>An ordinary review admits only Passed/Exited/0; a diagnosis also admits Failed/Exited/nonzero. Any other status, outcome
    /// or exit code, an unknown enum or a rejected exit-code storage is contradictory for the set.</summary>
    private static bool TryClassifyTerminal(VerificationExecutionRow execution, bool diagnosis, out bool failed, out int exitCode)
    {
        failed = false;
        exitCode = 0;
        if (execution.OutcomeCode != 1 || !execution.ExitCodePresent || execution.ExitCode is not { } code)
        {
            return false;
        }

        exitCode = code;
        switch (execution.StatusCode)
        {
            case 1 when code == 0:
                return true;
            case 2 when diagnosis && code != 0:
                failed = true;
                return true;
            default:
                return false;
        }
    }

    private static bool IsCommandName(VerificationExecutionRow execution) =>
        !execution.CommandNameOversized
        && !string.IsNullOrWhiteSpace(execution.CommandName)
        && execution.CommandName.Length <= MaximumCommandNameLength;

    private static bool IsFingerprint(string? value) =>
        value is { Length: FingerprintLength } && value.All(character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
}
