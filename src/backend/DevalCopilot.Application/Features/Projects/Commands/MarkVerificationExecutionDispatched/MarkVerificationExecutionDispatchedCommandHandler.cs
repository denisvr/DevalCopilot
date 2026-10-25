using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Commands.MarkVerificationExecutionDispatched;

public sealed class MarkVerificationExecutionDispatchedCommandHandler(
    IDevalCopilotDbContext dbContext, TimeProvider timeProvider, LocalExecutionHostLaunch? hostLaunch = null)
    : ICommandHandler<MarkVerificationExecutionDispatchedCommand, Result<MarkVerificationExecutionDispatchedCommandResult>>
{
    /// <summary>The refusal code of an execution whose local execution sequence no longer authorizes its dispatch (ADR-0060).</summary>
    public const string LocalExecutionSequenceDispatchRefusedCode = VerificationDispatchAuthority.LocalExecutionSequenceDispatchRefusedCode;

    /// <summary>One short write-locked transaction: the first statement is a self-referential no-op write that takes the lock,
    /// the execution and its ownership are then re-read untracked and must agree with the expected snapshot, and only then is the
    /// single-use marker committed conditionally. Nothing external happens under the lock; a refusal leaves the execution pending.</summary>
    public async Task<Result<MarkVerificationExecutionDispatchedCommandResult>> HandleAsync(MarkVerificationExecutionDispatchedCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);
        var locked = await dbContext.VerificationExecutions
            .Where(candidate => candidate.Id == command.VerificationExecutionId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Status, candidate => candidate.Status), cancellationToken);
        var execution = locked == 1
            ? await VerificationDispatchAuthority.ReadExecutionAsync(dbContext, command.VerificationExecutionId, cancellationToken)
            : null;
        if (execution is null)
        {
            return Result<MarkVerificationExecutionDispatchedCommandResult>.Failure(Error.NotFound("verification.execution_not_found", "This verification execution was not found."));
        }

        if (execution.Status != VerificationExecutionStatus.Running)
        {
            return Result<MarkVerificationExecutionDispatchedCommandResult>.Failure(Error.Conflict("verification.execution_not_dispatchable", "This verification execution cannot be dispatched."));
        }

        if (execution.DispatchedAtUtc.HasValue)
        {
            return Result<MarkVerificationExecutionDispatchedCommandResult>.Failure(Error.Conflict("verification.execution_already_dispatched", "This verification execution was already dispatched."));
        }

        if (await VerificationDispatchAuthority.ConfirmAsync(dbContext, execution, command.Expected, cancellationToken, timeProvider) is { } refusal)
        {
            return Result<MarkVerificationExecutionDispatchedCommandResult>.Failure(refusal);
        }

        var dispatchedAtUtc = timeProvider.GetUtcNow();

        // ADR-0065: the dispatch witness of an exact linked verification commits atomically with this marker, in this same transaction,
        // and its opaque ticket is returned only after the commit below. The linked step is decided again here under the same write lock
        // the authority above used; an association that cannot be proved refuses the dispatch before anything launches.
        LocalExecutionDispatchTicket? ticket = null;
        var linked = await LocalExecutionDispatchGate.EvaluateVerificationAsync(dbContext, execution, dispatchedAtUtc, cancellationToken);
        if (linked.Verdict == LocalExecutionDispatchGate.Verdict.Dispatchable)
        {
            ticket = await LocalExecutionDispatchWitnessing.RecordVerificationAsync(
                dbContext, hostLaunch ?? LocalExecutionHostLaunch.ProcessWide, linked, execution, dispatchedAtUtc, cancellationToken);
        }

        if (linked.Verdict == LocalExecutionDispatchGate.Verdict.Refused
            || (linked.Verdict == LocalExecutionDispatchGate.Verdict.Dispatchable && ticket is null))
        {
            return Result<MarkVerificationExecutionDispatchedCommandResult>.Failure(
                Error.Conflict(LocalExecutionSequenceDispatchRefusedCode, "The local execution sequence no longer authorizes this verification execution."));
        }

        var updated = await dbContext.VerificationExecutions
            .Where(candidate => candidate.Id == command.VerificationExecutionId
                && candidate.Status == VerificationExecutionStatus.Running
                && candidate.DispatchedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.DispatchedAtUtc, dispatchedAtUtc), cancellationToken);
        if (updated != 1)
        {
            return Result<MarkVerificationExecutionDispatchedCommandResult>.Failure(Error.Conflict("verification.execution_already_dispatched", "This verification execution was already dispatched."));
        }

        if (ticket is not null)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return Result<MarkVerificationExecutionDispatchedCommandResult>.Success(new MarkVerificationExecutionDispatchedCommandResult(ticket));
    }
}
