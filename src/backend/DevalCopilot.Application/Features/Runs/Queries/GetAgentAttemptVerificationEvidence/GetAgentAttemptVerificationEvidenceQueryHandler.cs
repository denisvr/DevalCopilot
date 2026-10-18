using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptVerificationEvidence;

public sealed class GetAgentAttemptVerificationEvidenceQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetAgentAttemptVerificationEvidenceQuery, Result<GetAgentAttemptVerificationEvidenceQueryResult>>
{
    /// <summary>The most members this inspection judges. It bounds this read only: it is not a claim bound, and a larger set is reported
    /// as unavailable, never as invalid.</summary>
    public const int MaximumInspectedMembers = RecordedVerificationMembershipReader.MaximumInspectedMembers;

    /// <summary>The two allowlisted streams of each member, so at most 64 metadata rows.</summary>
    public const int StreamsPerMember = 2;

    public async Task<Result<GetAgentAttemptVerificationEvidenceQueryResult>> HandleAsync(
        GetAgentAttemptVerificationEvidenceQuery query, CancellationToken cancellationToken)
    {
        var reading = await RecordedVerificationMembershipReader.ReadAsync(dbContext, query.RunId, query.AttemptId, cancellationToken);
        if (!reading.Found)
        {
            return Result<GetAgentAttemptVerificationEvidenceQueryResult>.Failure(AgentAttemptVerificationErrors.NotFound());
        }

        if (reading.Membership is not { } membership)
        {
            return Result<GetAgentAttemptVerificationEvidenceQueryResult>.Success(
                GetAgentAttemptVerificationEvidenceQueryResult.Unavailable(query.RunId, query.AttemptId));
        }

        // Metadata only, of the allowlisted streams of exactly the proved executions; one more row than the set can hold proves a damaged
        // set of rows, and then no stream of it is admitted. Paths, hashes and bytes are never selected here.
        var bound = membership.Members.Count * StreamsPerMember;
        var executionIds = membership.Members.Select(member => member.ExecutionId).ToArray();
        var outputs = await dbContext.VerificationOutputArtifacts.AsNoTracking()
            .Where(output => executionIds.Contains(output.VerificationExecutionId))
            .WhereAllowlisted()
            .Take(bound + 1)
            .ProjectDescriptor()
            .ToArrayAsync(cancellationToken);

        var members = membership.Members
            .Select(member => new AgentAttemptVerificationMember(
                member.Sequence,
                member.CommandId,
                member.ExecutionId,
                member.ExecutionNumber,
                member.CommandName,
                member.Failed ? AgentAttemptVerificationMemberStatus.Failed : AgentAttemptVerificationMemberStatus.Passed,
                member.ExitCode,
                member.ClaimedAtUtc,
                member.DispatchedAtUtc,
                member.CompletedAtUtc,
                Describe(outputs, bound, member.ExecutionId, RecordedVerificationOutputAdmission.StandardOutputCode),
                Describe(outputs, bound, member.ExecutionId, RecordedVerificationOutputAdmission.StandardErrorCode)))
            .ToArray();

        var identity = membership.Identity;
        return Result<GetAgentAttemptVerificationEvidenceQueryResult>.Success(new GetAgentAttemptVerificationEvidenceQueryResult(
            query.RunId,
            query.AttemptId,
            AgentAttemptVerificationEvidenceStatus.Recorded,
            new AgentAttemptVerificationEvidenceHeader(
                membership.AttemptId, membership.AttemptNumber, identity.Role, identity.Provider, identity.ResponseContract),
            new AgentAttemptVerificationEvidenceCheckpoint(membership.CheckpointId, membership.CheckpointNumber),
            members.Length,
            members));
    }

    private static RecordedVerificationOutputDescriptor Describe(
        VerificationOutputDescriptorRow[] outputs, int bound, Guid executionId, int purposeCode) =>
        outputs.Length > bound
            ? RecordedVerificationOutputDescriptor.Unavailable
            : RecordedVerificationOutputAdmission.Admit(
                outputs.Where(output => output.ExecutionId == executionId && output.PurposeCode == purposeCode).ToArray());
}
