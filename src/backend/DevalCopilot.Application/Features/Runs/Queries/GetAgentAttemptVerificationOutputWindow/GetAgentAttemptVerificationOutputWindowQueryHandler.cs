using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptVerificationOutputWindow;

public sealed class GetAgentAttemptVerificationOutputWindowQueryHandler(IDevalCopilotDbContext dbContext, IArtifactStore artifactStore)
    : IQueryHandler<GetAgentAttemptVerificationOutputWindowQuery, Result<GetAgentAttemptVerificationOutputWindowQueryResult>>
{
    public async Task<Result<GetAgentAttemptVerificationOutputWindowQueryResult>> HandleAsync(
        GetAgentAttemptVerificationOutputWindowQuery query, CancellationToken cancellationToken)
    {
        // The validator refuses these before dispatch; the handler enforces them again so no caller can reach the store outside them.
        if (query.FromOffset is < 0 or > GetAgentAttemptVerificationOutputWindowQuery.MaximumOffset)
        {
            return Result<GetAgentAttemptVerificationOutputWindowQueryResult>.Failure(GetAgentAttemptVerificationOutputWindowErrors.InvalidOffset());
        }

        if (query.MaxBytes is < GetAgentAttemptVerificationOutputWindowQuery.MinimumMaxBytes
            or > GetAgentAttemptVerificationOutputWindowQuery.MaximumMaxBytes)
        {
            return Result<GetAgentAttemptVerificationOutputWindowQueryResult>.Failure(
                GetAgentAttemptVerificationOutputWindowErrors.InvalidMaxBytes());
        }

        if (query.Stream is not (VerificationOutputPurpose.StandardOutput or VerificationOutputPurpose.StandardError))
        {
            return Result<GetAgentAttemptVerificationOutputWindowQueryResult>.Failure(AgentAttemptVerificationErrors.NotFound());
        }

        // The whole membership is proved again for every window; nothing an earlier read established is trusted.
        var reading = await RecordedVerificationMembershipReader.ReadAsync(dbContext, query.RunId, query.AttemptId, cancellationToken);
        if (!reading.Found)
        {
            return Result<GetAgentAttemptVerificationOutputWindowQueryResult>.Failure(AgentAttemptVerificationErrors.NotFound());
        }

        if (reading.Membership is not { } membership)
        {
            return Refused(query, AgentAttemptVerificationOutputWindowStatus.Unavailable);
        }

        if (!membership.Members.Any(member => member.ExecutionId == query.ExecutionId))
        {
            return Result<GetAgentAttemptVerificationOutputWindowQueryResult>.Failure(AgentAttemptVerificationErrors.NotFound());
        }

        // Exactly the requested stream of exactly this execution; one more row than can exist proves damaged storage.
        var purpose = query.Stream;
        var purposeCode = RecordedVerificationOutputAdmission.PurposeCode(purpose);
        var rows = await dbContext.VerificationOutputArtifacts.AsNoTracking()
            .Where(output => output.VerificationExecutionId == query.ExecutionId && output.Purpose == purpose)
            .Take(2)
            .ProjectStorage()
            .ToArrayAsync(cancellationToken);
        if (rows.Any(row => row.ExecutionId != query.ExecutionId || row.PurposeCode != purposeCode))
        {
            return Refused(query, AgentAttemptVerificationOutputWindowStatus.Unavailable);
        }

        var descriptor = RecordedVerificationOutputAdmission.Admit(rows.Select(row => row.Descriptor).ToArray());
        switch (descriptor.Status)
        {
            case RecordedVerificationOutputDescriptorStatus.NotRecorded:
                return Refused(query, AgentAttemptVerificationOutputWindowStatus.NotRecorded);
            case RecordedVerificationOutputDescriptorStatus.Unavailable:
                return Refused(query, AgentAttemptVerificationOutputWindowStatus.Unavailable);
        }

        var storage = rows[0];
        if (!RecordedVerificationOutputAdmission.IsAdmissibleStorage(storage))
        {
            return Refused(query, AgentAttemptVerificationOutputWindowStatus.Unavailable);
        }

        var byteLength = descriptor.ByteLength!.Value;
        if (query.FromOffset > byteLength)
        {
            return Result<GetAgentAttemptVerificationOutputWindowQueryResult>.Failure(
                GetAgentAttemptVerificationOutputWindowErrors.OffsetBeyondLength());
        }

        // The admitted values pass unchanged to the store's own containment, whole-file length/hash proof and UTF-8 cursor.
        var window = await artifactStore.VerifyAndReadSealedAsync(
            storage.RelativeStoragePath!, byteLength, storage.ContentHash!, query.FromOffset, query.MaxBytes, cancellationToken);
        return window.Status switch
        {
            SealedReadStatus.Ok => Result<GetAgentAttemptVerificationOutputWindowQueryResult>.Success(new(
                query.RunId, query.AttemptId, query.ExecutionId, purpose, AgentAttemptVerificationOutputWindowStatus.Ok, descriptor,
                window.Text, window.NextOffset, byteLength)),
            SealedReadStatus.Missing => Refused(query, AgentAttemptVerificationOutputWindowStatus.Missing),
            _ => Refused(query, AgentAttemptVerificationOutputWindowStatus.IntegrityMismatch),
        };
    }

    private static Result<GetAgentAttemptVerificationOutputWindowQueryResult> Refused(
        GetAgentAttemptVerificationOutputWindowQuery query, AgentAttemptVerificationOutputWindowStatus status) =>
        Result<GetAgentAttemptVerificationOutputWindowQueryResult>.Success(
            GetAgentAttemptVerificationOutputWindowQueryResult.Refused(query, status));
}
