using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetEligibleProcessAttempts;

public sealed class GetEligibleProcessAttemptsQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetEligibleProcessAttemptsQuery, IReadOnlyList<EligibleProcessAttempt>>
{
    public async Task<IReadOnlyList<EligibleProcessAttempt>> HandleAsync(
        GetEligibleProcessAttemptsQuery query,
        CancellationToken cancellationToken)
    {
        // SQLite cannot translate ORDER BY over DateTimeOffset server-side, and the eligible
        // set is always small, so oldest-first ordering happens client-side after a narrow
        // projection — the same pattern GetEligibleSimulatedRunsQueryHandler uses.
        //
        // Joined against Runs (rather than only filtering Attempts) so an attempt whose run
        // is missing or no longer Running — an inconsistent state reconciliation should have
        // already resolved — is never handed to the supervisor for execution.
        //
        // ProcessDispatchedAtUtc == null excludes any attempt already durably marked as
        // dispatched: the external command runs at most once per attempt, so one that was
        // dispatched but never reached a terminal result (a stalled or failed recording) must
        // never be handed to the supervisor again — it stays Running, un-terminal, until
        // restart reconciliation marks it Interrupted.
        var attempts = await dbContext.Attempts
            .AsNoTracking()
            .Where(attempt =>
                attempt.Kind == AttemptKind.Process
                && attempt.Status == AttemptStatus.Running
                && attempt.ProcessDispatchedAtUtc == null)
            .Join(
                dbContext.Runs.AsNoTracking().Where(run => run.Lifecycle == RunLifecycle.Running),
                attempt => attempt.RunId,
                run => run.Id,
                (attempt, run) => attempt)
            .Select(attempt => new
            {
                attempt.Id,
                attempt.RunId,
                attempt.ClaimedAtUtc,
                ExecutablePath = attempt.ProcessExecutablePath!,
                attempt.ProcessArguments,
                WorkingDirectory = attempt.ProcessWorkingDirectory!,
                ApprovedRoot = attempt.ProcessApprovedRoot!,
                Timeout = attempt.ProcessTimeout!.Value,
                MaxBytesPerStream = attempt.ProcessMaxBytesPerStream!.Value,
                MaxTotalCapturedBytes = attempt.ProcessMaxTotalCapturedBytes!.Value,
            })
            .ToListAsync(cancellationToken);

        return attempts
            .OrderBy(attempt => attempt.ClaimedAtUtc)
            .Select(attempt => new EligibleProcessAttempt(
                attempt.Id,
                attempt.RunId,
                attempt.ExecutablePath,
                attempt.ProcessArguments,
                attempt.WorkingDirectory,
                attempt.ApprovedRoot,
                attempt.Timeout,
                attempt.MaxBytesPerStream,
                attempt.MaxTotalCapturedBytes))
            .ToArray();
    }
}
