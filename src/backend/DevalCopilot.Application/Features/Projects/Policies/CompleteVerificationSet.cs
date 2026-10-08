using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Policies;

/// <summary>
/// The complete, currently enabled verification set a human may approve as one decision: for EVERY enabled recipe of the project,
/// its single latest execution bound to the exact current checkpoint and fingerprint, which must be a coherent clean Passed
/// completion whose snapshot still equals the current recipe. It is read untracked and fresh on every call, never from a bounded
/// history window, and never falls back to an older execution. Members are ordered by the host's CommandNumber. A project with more
/// than <see cref="MaximumMembers"/> enabled recipes has no admissible set (it is refused, never truncated). The rules are those of
/// the local-commit authority's verification membership, so a set approved here is exactly the set that authority later requires.
/// </summary>
internal static class CompleteVerificationSet
{
    public const int MaximumMembers = 32;

    internal enum Refusal
    {
        NoEnabledRecipes,
        TooManyRecipes,
        Incomplete,
    }

    internal sealed record Member(VerificationCommand Command, VerificationExecution Execution);

    internal sealed record Read(Refusal? Refusal, IReadOnlyList<Member> Members);

    public static async Task<Read> ReadAsync(
        IDevalCopilotDbContext dbContext, CheckpointReviewSource.Authority source, CancellationToken cancellationToken)
    {
        var commands = await dbContext.VerificationCommands.AsNoTracking()
            .Where(command => command.ProjectId == source.ProjectId && command.IsEnabled)
            .OrderBy(command => command.CommandNumber)
            .Take(MaximumMembers + 1)
            .ToListAsync(cancellationToken);
        if (commands.Count == 0)
        {
            return new Read(Refusal.NoEnabledRecipes, []);
        }

        if (commands.Count > MaximumMembers)
        {
            return new Read(Refusal.TooManyRecipes, []);
        }

        var members = new List<Member>(commands.Count);
        foreach (var command in commands)
        {
            var execution = await dbContext.VerificationExecutions.AsNoTracking()
                .Where(candidate => candidate.VerificationCommandId == command.Id
                    && candidate.GitCheckpointId == source.CheckpointId
                    && candidate.CheckpointFingerprintSha256 == source.FingerprintSha256)
                .OrderByDescending(candidate => candidate.ExecutionNumber)
                .FirstOrDefaultAsync(cancellationToken);
            if (execution is null || !IsCurrentCleanPass(command, execution, source))
            {
                return new Read(Refusal.Incomplete, []);
            }

            members.Add(new Member(command, execution));
        }

        return new Read(null, members);
    }

    private static bool IsCurrentCleanPass(VerificationCommand command, VerificationExecution execution, CheckpointReviewSource.Authority source) =>
        execution.ProjectId == source.ProjectId
        && execution.GitWorkspaceId == source.WorkspaceId
        && string.Equals(execution.WorkspacePath, source.WorkspacePath, StringComparison.Ordinal)
        && execution.Status == VerificationExecutionStatus.Passed
        && execution.Outcome == VerificationExecutionOutcome.Exited
        && execution.ExitCode == 0
        && execution.DispatchedAtUtc is not null
        && execution.CompletedAtUtc is not null
        && string.Equals(execution.CompletionFingerprintSha256, source.FingerprintSha256, StringComparison.Ordinal)
        && string.Equals(command.Name, execution.CommandName, StringComparison.Ordinal)
        && string.Equals(command.ExecutablePath, execution.ExecutablePath, StringComparison.Ordinal)
        && command.TimeoutSeconds == execution.TimeoutSeconds
        && command.Arguments.SequenceEqual(execution.Arguments, StringComparer.Ordinal);
}
