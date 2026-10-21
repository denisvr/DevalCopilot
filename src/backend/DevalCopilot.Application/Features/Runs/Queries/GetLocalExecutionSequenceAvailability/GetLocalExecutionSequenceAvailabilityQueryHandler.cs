using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequenceAvailability;

public sealed class GetLocalExecutionSequenceAvailabilityQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetLocalExecutionSequenceAvailabilityQuery, Result<GetLocalExecutionSequenceAvailabilityQueryResult>>
{
    public async Task<Result<GetLocalExecutionSequenceAvailabilityQueryResult>> HandleAsync(
        GetLocalExecutionSequenceAvailabilityQuery query, CancellationToken cancellationToken)
    {
        if (!await dbContext.Runs.AsNoTracking().AnyAsync(candidate => candidate.Id == query.RunId, cancellationToken))
        {
            return Result<GetLocalExecutionSequenceAvailabilityQueryResult>.Failure(LocalExecutionSequenceErrors.NotFound());
        }

        if (await dbContext.LocalExecutionSequences.AsNoTracking().AnyAsync(candidate => candidate.RunId == query.RunId, cancellationToken))
        {
            return Refused(query, LocalExecutionSequenceErrors.AlreadyArmedCode);
        }

        var reading = await LocalExecutionCandidateReader.ReadAsync(dbContext, query.RunId, query.PlanProposalMessageId, cancellationToken);
        if (reading.Error is { } error)
        {
            return Refused(query, error.Code);
        }

        var candidate = reading.Candidate!;
        var run = reading.Run!;
        var turns = run.ReadRequestedClaudeMaxTurns();
        var accountStop = run.ReadCodexAccountUsageStopPercent();
        var disclosure = new LocalExecutionSequenceAvailabilityDisclosure(
            run.RequestedCodexModel,
            run.RequestedCodexEffort,
            run.RequestedClaudeModel,
            run.RequestedClaudeEffort,
            turns.Value,
            accountStop.Value,
            run.CodexTokenStopThreshold,
            run.ClaudeTokenStopThreshold);
        var recipes = candidate.Recipes
            .Select(recipe => new LocalExecutionSequenceAvailabilityRecipe(
                recipe.Ordinal, recipe.CommandId, recipe.CommandNumber, recipe.Name, recipe.TimeoutSeconds))
            .ToArray();
        return Result<GetLocalExecutionSequenceAvailabilityQueryResult>.Success(new GetLocalExecutionSequenceAvailabilityQueryResult(
            true,
            null,
            query.RunId,
            query.PlanProposalMessageId,
            candidate.PlanAuthority,
            candidate.WorkspaceId,
            candidate.CheckpointId,
            candidate.FingerprintSha256,
            candidate.ConfirmationSha256,
            candidate.CorrectionAllowance,
            LocalExecutionSequencePolicy.MaximumSteps(recipes.Length),
            recipes,
            disclosure));
    }

    private static Result<GetLocalExecutionSequenceAvailabilityQueryResult> Refused(
        GetLocalExecutionSequenceAvailabilityQuery query, string code) =>
        Result<GetLocalExecutionSequenceAvailabilityQueryResult>.Success(new GetLocalExecutionSequenceAvailabilityQueryResult(
            false, code, query.RunId, query.PlanProposalMessageId, null, null, null, null, null, null, null, null, null));
}
