using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Commands.ReconcileInterruptedVerificationExecutions;

public sealed class ReconcileInterruptedVerificationExecutionsCommandHandler(IDevalCopilotDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<ReconcileInterruptedVerificationExecutionsCommand, Result<int>>
{
    public async Task<Result<int>> HandleAsync(
        ReconcileInterruptedVerificationExecutionsCommand command, CancellationToken cancellationToken)
    {
        // ADR-0060: an execution linked to a local execution sequence is closed or quarantined by the sequence's own startup command.
        var executions = await dbContext.VerificationExecutions
            .Where(execution => execution.Status == VerificationExecutionStatus.Running
                && !dbContext.LocalExecutionSequenceSteps.Any(step => step.OperationId == execution.Id))
            .ToListAsync(cancellationToken);
        var nowUtc = timeProvider.GetUtcNow();
        foreach (var execution in executions)
        {
            execution.Interrupt(nowUtc);
        }

        return Result<int>.Success(executions.Count);
    }
}
