using DevalCopilot.Application.Data;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;

/// <summary>
/// The one place the internal step ticket is created from a committed reservation. The ticket's closed shape: a Verification step carries
/// the exact pinned recipe (the project verification command its recipe ordinal selects), and every Agent step carries none. A queued
/// reservation's self-exclusion proof requires exactly this shape, so a ticket with the recipe omitted or substituted never excludes anything.
/// </summary>
internal static class LocalExecutionStepTicketFactory
{
    public static async Task<LocalExecutionStepTicket> CreateAsync(
        IDevalCopilotDbContext dbContext, LocalExecutionSequenceStep step, CancellationToken cancellationToken)
    {
        Guid? recipeId = null;
        if (step.Kind == LocalExecutionStepKind.Verification)
        {
            recipeId = await dbContext.LocalExecutionSequenceRecipes.AsNoTracking()
                .Where(recipe => recipe.SequenceId == step.SequenceId && recipe.Ordinal == step.RecipeOrdinal)
                .Select(recipe => (Guid?)recipe.VerificationCommandId)
                .SingleOrDefaultAsync(cancellationToken);
        }

        return new LocalExecutionStepTicket(
            step.SequenceId,
            step.Id,
            step.Ordinal,
            step.Kind,
            step.ReservedOperationId,
            step.PreparationOwnerToken,
            step.SourceCheckpointId,
            step.ReportMessageId,
            step.SubjectAttemptId,
            recipeId);
    }
}
