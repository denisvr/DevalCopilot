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
        var executions = await dbContext.VerificationExecutions
            .Where(execution => execution.Status == VerificationExecutionStatus.Running)
            .ToListAsync(cancellationToken);
        var nowUtc = timeProvider.GetUtcNow();
        foreach (var execution in executions)
        {
            execution.Interrupt(nowUtc);
        }

        return Result<int>.Success(executions.Count);
    }
}
