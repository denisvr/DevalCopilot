using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.Features.Runs.GetAgentAttemptVerificationEvidence;
using DevalCopilot.Api.Features.Runs.GetLocalExecutionSequenceInspection;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionStepInspection;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetLocalExecutionStepInspection;

/// <summary>
/// Protected, read-only, bodyless selected operation of one recorded step of a run's local execution sequence (ADR-0061). It re-proves the
/// bounded structure and the exact step on every request and returns, for a linked Agent step, only the existing immutable attempt header,
/// and, for a linked verification, its proved facts and two sealed stream descriptors. It accepts no query key and no body and reads no
/// artifact byte. An unknown run, a run without a sequence and a step outside that run's sequence are one fixed 404 that repeats no value.
/// </summary>
public sealed class GetLocalExecutionStepInspectionEndpoint(IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails)
    : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/local-execution-sequence/inspection/steps/{stepId:guid}")]
    [TypeFilter(typeof(LocalExecutionInspectionBodylessGuardFilter))]
    [ProducesResponseType<GetLocalExecutionStepInspectionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetLocalExecutionStepInspectionResponse>> GetLocalExecutionStepInspection(
        [FromRoute] Guid runId, [FromRoute] Guid stepId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetLocalExecutionStepInspectionQuery(runId, stepId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetLocalExecutionStepInspectionResponse(
            value.RunId,
            value.SequenceId,
            value.StepId,
            value.Status.ToString(),
            value.Step is { } step ? GetLocalExecutionSequenceInspectionEndpoint.Map(step) : null,
            value.Agent is { } agent
                ? new LocalExecutionInspectionAgentResponse(
                    agent.Id, agent.AttemptNumber, agent.Role.ToString(), agent.Provider.ToString(), agent.ResponseContract.ToString())
                : null,
            value.Verification is { } verification ? Map(verification) : null));
    }

    private static LocalExecutionInspectionVerificationResponse Map(LocalExecutionVerificationInspection verification)
    {
        var facts = verification.Facts;
        return new LocalExecutionInspectionVerificationResponse(
            facts.ExecutionId,
            facts.ExecutionNumber,
            facts.CommandName,
            facts.Status.ToString(),
            facts.Outcome?.ToString(),
            facts.ExitCode,
            GetLocalExecutionSequenceInspectionEndpoint.Time(facts.ClaimedAtUtc),
            facts.DispatchedAtUtc is { } dispatched ? GetLocalExecutionSequenceInspectionEndpoint.Time(dispatched) : null,
            facts.CompletedAtUtc is { } completed ? GetLocalExecutionSequenceInspectionEndpoint.Time(completed) : null,
            GetAgentAttemptVerificationEvidenceEndpoint.MapStream(verification.StandardOutput),
            GetAgentAttemptVerificationEvidenceEndpoint.MapStream(verification.StandardError));
    }
}
