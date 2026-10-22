using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionVerificationOutputWindow;

public sealed class GetLocalExecutionVerificationOutputWindowQueryHandler(IDevalCopilotDbContext dbContext, IArtifactStore artifactStore)
    : IQueryHandler<GetLocalExecutionVerificationOutputWindowQuery, Result<GetLocalExecutionVerificationOutputWindowQueryResult>>
{
    public async Task<Result<GetLocalExecutionVerificationOutputWindowQueryResult>> HandleAsync(
        GetLocalExecutionVerificationOutputWindowQuery query, CancellationToken cancellationToken)
    {
        // The validator refuses these before dispatch; the handler enforces them again so no caller can reach the store outside them.
        if (query.FromOffset is < 0 or > GetLocalExecutionVerificationOutputWindowQuery.MaximumOffset)
        {
            return Result<GetLocalExecutionVerificationOutputWindowQueryResult>.Failure(
                GetLocalExecutionVerificationOutputWindowErrors.InvalidOffset());
        }

        if (query.MaxBytes is < GetLocalExecutionVerificationOutputWindowQuery.MinimumMaxBytes
            or > GetLocalExecutionVerificationOutputWindowQuery.MaximumMaxBytes)
        {
            return Result<GetLocalExecutionVerificationOutputWindowQueryResult>.Failure(
                GetLocalExecutionVerificationOutputWindowErrors.InvalidMaxBytes());
        }

        if (query.Stream is not (VerificationOutputPurpose.StandardOutput or VerificationOutputPurpose.StandardError))
        {
            return NotFound();
        }

        // The whole structure is proved again for every window; nothing an earlier read established is trusted.
        var reading = await LocalExecutionInspectionReader.ReadAsync(dbContext, query.RunId, cancellationToken);
        if (reading.Kind is LocalExecutionInspectionReadingKind.RunNotFound or LocalExecutionInspectionReadingKind.NotRecorded
            || reading.SequenceId is not { } sequenceId
            || await LocalExecutionInspectionReader.FindStepKindAsync(dbContext, sequenceId, query.StepId, cancellationToken)
                != LocalExecutionStepKind.Verification)
        {
            return NotFound();
        }

        if (reading.Structure is not { } structure
            || structure.Steps.FirstOrDefault(candidate => candidate.StepId == query.StepId) is not { } step)
        {
            return Refused(query, sequenceId, null, LocalExecutionVerificationOutputWindowStatus.Unavailable);
        }

        // A coherent verification step with no actual link, or an operation that is not proved against its pinned recipe, never reads a
        // byte and never names the reserved identity as an execution.
        var facts = await LocalExecutionOperationProof.ProveVerificationAsync(dbContext, structure, step, cancellationToken);
        if (facts is null)
        {
            return Refused(query, sequenceId, null, LocalExecutionVerificationOutputWindowStatus.Unavailable);
        }

        var executionId = facts.ExecutionId;

        // Exactly the requested stream of exactly the proved execution; one more row than can exist proves damaged storage.
        var purpose = query.Stream;
        var purposeCode = RecordedVerificationOutputAdmission.PurposeCode(purpose);
        var rows = await dbContext.VerificationOutputArtifacts.AsNoTracking()
            .Where(output => output.VerificationExecutionId == executionId && output.Purpose == purpose)
            .Take(2)
            .ProjectStorage()
            .ToArrayAsync(cancellationToken);
        if (rows.Any(row => row.ExecutionId != executionId || row.PurposeCode != purposeCode))
        {
            return Refused(query, sequenceId, executionId, LocalExecutionVerificationOutputWindowStatus.Unavailable);
        }

        var descriptor = RecordedVerificationOutputAdmission.Admit(rows.Select(row => row.Descriptor).ToArray());
        switch (descriptor.Status)
        {
            case RecordedVerificationOutputDescriptorStatus.NotRecorded:
                return Refused(query, sequenceId, executionId, LocalExecutionVerificationOutputWindowStatus.NotRecorded);
            case RecordedVerificationOutputDescriptorStatus.Unavailable:
                return Refused(query, sequenceId, executionId, LocalExecutionVerificationOutputWindowStatus.Unavailable);
        }

        var storage = rows[0];
        if (!RecordedVerificationOutputAdmission.IsAdmissibleStorage(storage))
        {
            return Refused(query, sequenceId, executionId, LocalExecutionVerificationOutputWindowStatus.Unavailable);
        }

        var byteLength = descriptor.ByteLength!.Value;
        if (query.FromOffset > byteLength)
        {
            return Result<GetLocalExecutionVerificationOutputWindowQueryResult>.Failure(
                GetLocalExecutionVerificationOutputWindowErrors.OffsetBeyondLength());
        }

        // The admitted values pass unchanged to the store's own containment, whole-file length/hash proof and UTF-8 cursor.
        var window = await artifactStore.VerifyAndReadSealedAsync(
            storage.RelativeStoragePath!, byteLength, storage.ContentHash!, query.FromOffset, query.MaxBytes, cancellationToken);
        return window.Status switch
        {
            SealedReadStatus.Ok => Result<GetLocalExecutionVerificationOutputWindowQueryResult>.Success(new(
                query.RunId,
                sequenceId,
                query.StepId,
                executionId,
                purpose,
                LocalExecutionVerificationOutputWindowStatus.Ok,
                descriptor,
                window.Text,
                window.NextOffset,
                byteLength)),
            SealedReadStatus.Missing => Refused(query, sequenceId, executionId, LocalExecutionVerificationOutputWindowStatus.Missing),
            _ => Refused(query, sequenceId, executionId, LocalExecutionVerificationOutputWindowStatus.IntegrityMismatch),
        };
    }

    private static Result<GetLocalExecutionVerificationOutputWindowQueryResult> NotFound() =>
        Result<GetLocalExecutionVerificationOutputWindowQueryResult>.Failure(LocalExecutionInspectionErrors.NotFound());

    private static Result<GetLocalExecutionVerificationOutputWindowQueryResult> Refused(
        GetLocalExecutionVerificationOutputWindowQuery query,
        Guid sequenceId,
        Guid? executionId,
        LocalExecutionVerificationOutputWindowStatus status) =>
        Result<GetLocalExecutionVerificationOutputWindowQueryResult>.Success(
            GetLocalExecutionVerificationOutputWindowQueryResult.Refused(query, sequenceId, executionId, status));
}
