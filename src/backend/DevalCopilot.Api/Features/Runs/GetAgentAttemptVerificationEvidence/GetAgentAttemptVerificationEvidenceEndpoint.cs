using System.Globalization;
using Devalente.Shared.AspNetCore.Mvc;
using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptVerificationEvidence;
using Microsoft.AspNetCore.Mvc;

namespace DevalCopilot.Api.Features.Runs.GetAgentAttemptVerificationEvidence;

/// <summary>
/// Protected, read-only, bodyless inspection of the recorded verification membership of one CodeReviewer attempt (ADR-0052),
/// independent of any message the attempt produced. An unknown run and a missing, foreign or non-Agent attempt are one fixed 404 in the
/// shared Problem Details contract that repeats no value. The answer is metadata only: no path, hash, byte, digest or current recipe.
/// </summary>
public sealed class GetAgentAttemptVerificationEvidenceEndpoint(IApplicationMediator mediator, IResultProblemDetailsFactory problemDetails)
    : RunsBaseEndpoint
{
    [HttpGet("{runId:guid}/agent-attempts/{attemptId:guid}/verification-evidence")]
    [ProducesResponseType<GetAgentAttemptVerificationEvidenceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetAgentAttemptVerificationEvidenceResponse>> GetAgentAttemptVerificationEvidence(
        [FromRoute] Guid runId, [FromRoute] Guid attemptId, CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new GetAgentAttemptVerificationEvidenceQuery(runId, attemptId), cancellationToken);
        if (result.IsFailure)
        {
            return problemDetails.CreateResponse(result, HttpContext);
        }

        var value = result.Value;
        return Ok(new GetAgentAttemptVerificationEvidenceResponse(
            value.RunId,
            value.AttemptId,
            value.Status.ToString(),
            value.Attempt is { } attempt
                ? new AgentAttemptVerificationHeaderResponse(
                    attempt.Id, attempt.AttemptNumber, attempt.Role.ToString(), attempt.Provider.ToString(), attempt.ResponseContract.ToString())
                : null,
            value.Checkpoint is { } checkpoint
                ? new AgentAttemptVerificationCheckpointResponse(checkpoint.Id, checkpoint.CheckpointNumber)
                : null,
            value.MemberTotalCount,
            value.Members.Select(Map).ToArray()));
    }

    internal static AgentAttemptVerificationStreamResponse MapStream(RecordedVerificationOutputDescriptor descriptor) => new(
        descriptor.Status.ToString(), descriptor.ArtifactId, descriptor.ByteLength, descriptor.CaptureOutcome?.ToString(), descriptor.Truncated);

    private static string Instant(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static AgentAttemptVerificationMemberResponse Map(AgentAttemptVerificationMember member) => new(
        member.Sequence,
        member.CommandId,
        member.ExecutionId,
        member.ExecutionNumber,
        member.CommandName,
        member.Status.ToString(),
        member.ExitCode,
        Instant(member.ClaimedAtUtc),
        Instant(member.DispatchedAtUtc),
        Instant(member.CompletedAtUtc),
        MapStream(member.StandardOutput),
        MapStream(member.StandardError));
}
