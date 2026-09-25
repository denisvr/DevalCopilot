using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Queries.GetRunCockpit;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetRunCockpit;

public sealed class GetRunCockpitEndpoint(
    IApplicationMediator mediator,
    IResultProblemDetailsFactory problemDetails) : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/cockpit")]
    public async Task<ActionResult<GetRunCockpitResponse>> GetRunCockpit(Guid runId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetRunCockpitQuery(runId), cancellationToken);

        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        var stageMap = value.StageMap
            .Select(entry => new StageMapEntryResponse(entry.Stage.ToString(), entry.IsCompleted, entry.IsActive))
            .ToArray();

        return Ok(
            new GetRunCockpitResponse(
                value.RunId,
                value.ProjectId,
                value.ProjectName,
                value.ExecutionNumber,
                value.Objective,
                value.Lifecycle.ToString(),
                value.Stage.ToString(),
                ParticipantIdentityResponse.FromDomain(value.ActiveParticipant),
                value.AutonomousDurationSeconds,
                value.LatestSequence,
                stageMap,
                value.CanPause,
                value.CanStop,
                value.LatestAgentAttempt is { } attempt
                    ? new RunCockpitAgentAttemptResponse(
                        attempt.AttemptId,
                        attempt.AttemptNumber,
                        attempt.Role?.ToString(),
                        attempt.Provider?.ToString(),
                        attempt.Status.ToString(),
                        attempt.Outcome?.ToString(),
                        attempt.DispatchedAtUtc,
                        AgentProcessExecutionResponse.FromAttempt(attempt.ProcessExecution, attempt.Timeout),
                        AgentTokenUsageResponse.FromAttempt(attempt.TokenUsage))
                    : null,
                RunTokenUsageSummaryResponse.FromSummary(value.TokenUsageSummary),
                value.MaximumAgentAttempts,
                value.AgentAttemptsUsed,
                value.AgentBudgetExhausted,
                AgentInvocationTimeBudgetResponse.FromDomain(value.AgentInvocationTimeBudget)));
    }
}
