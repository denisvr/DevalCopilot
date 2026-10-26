using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Policies.ProjectVerificationInspection;
using DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;
using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationOutputWindow;

public sealed class GetProjectVerificationOutputWindowQueryHandler(IDevalCopilotDbContext dbContext, IArtifactStore artifactStore)
    : IQueryHandler<GetProjectVerificationOutputWindowQuery, Result<GetProjectVerificationOutputWindowQueryResult>>
{
    public async Task<Result<GetProjectVerificationOutputWindowQueryResult>> HandleAsync(
        GetProjectVerificationOutputWindowQuery query, CancellationToken cancellationToken)
    {
        // The validator refuses these before dispatch; the handler enforces them again so no caller can reach the store outside them.
        if (query.FromOffset is < 0 or > GetProjectVerificationOutputWindowQuery.MaximumOffset)
        {
            return Result<GetProjectVerificationOutputWindowQueryResult>.Failure(new ValidationError(
                ProjectVerificationInspectionErrors.InvalidOffsetCode,
                ProjectVerificationInspectionErrors.InvalidOffsetMessage,
                nameof(GetProjectVerificationOutputWindowQuery.FromOffset)));
        }

        if (query.MaxBytes is < GetProjectVerificationOutputWindowQuery.MinimumMaxBytes
            or > GetProjectVerificationOutputWindowQuery.MaximumMaxBytes)
        {
            return Result<GetProjectVerificationOutputWindowQueryResult>.Failure(new ValidationError(
                ProjectVerificationInspectionErrors.InvalidMaxBytesCode,
                ProjectVerificationInspectionErrors.InvalidMaxBytesMessage,
                nameof(GetProjectVerificationOutputWindowQuery.MaxBytes)));
        }

        if (query.Stream is not (VerificationOutputPurpose.StandardOutput or VerificationOutputPurpose.StandardError))
        {
            return Result<GetProjectVerificationOutputWindowQueryResult>.Failure(ProjectVerificationInspectionErrors.NotFound());
        }

        // The selected execution is proved again for every window; nothing an earlier read established is trusted.
        var reading = await ProjectVerificationInspectionReader.ReadAsync(dbContext, query.ProjectId, query.ExecutionId, cancellationToken);
        if (reading.Kind == ProjectVerificationInspectionReadingKind.NotFound)
        {
            return Result<GetProjectVerificationOutputWindowQueryResult>.Failure(ProjectVerificationInspectionErrors.NotFound());
        }

        if (reading.Execution is null)
        {
            return Refused(query, ProjectVerificationOutputWindowStatus.Unavailable);
        }

        // Exactly the requested stream of exactly this execution; a second row proves damaged storage.
        var storageReading = await ProjectVerificationOutputReader.ReadStorageAsync(
            dbContext, query.ExecutionId, query.Stream, cancellationToken);
        var descriptor = storageReading.Descriptor;
        switch (descriptor.Status)
        {
            case RecordedVerificationOutputDescriptorStatus.NotRecorded:
                return Refused(query, ProjectVerificationOutputWindowStatus.NotRecorded);
            case RecordedVerificationOutputDescriptorStatus.Unavailable:
                return Refused(query, ProjectVerificationOutputWindowStatus.Unavailable);
        }

        if (storageReading.Storage is not { } storage || !RecordedVerificationOutputAdmission.IsAdmissibleStorage(storage))
        {
            return Refused(query, ProjectVerificationOutputWindowStatus.Unavailable);
        }

        var byteLength = descriptor.ByteLength!.Value;
        if (query.FromOffset > byteLength)
        {
            return Result<GetProjectVerificationOutputWindowQueryResult>.Failure(ProjectVerificationInspectionErrors.OffsetBeyondLength());
        }

        // The admitted values pass unchanged to the store's own containment, whole-file length/hash proof and UTF-8 cursor.
        var window = await artifactStore.VerifyAndReadSealedAsync(
            storage.RelativeStoragePath!, byteLength, storage.ContentHash!, query.FromOffset, query.MaxBytes, cancellationToken);
        return window.Status switch
        {
            SealedReadStatus.Ok => Result<GetProjectVerificationOutputWindowQueryResult>.Success(new(
                query.ProjectId, query.ExecutionId, query.Stream, ProjectVerificationOutputWindowStatus.Ok, descriptor,
                window.Text, window.NextOffset, byteLength)),
            SealedReadStatus.Missing => Refused(query, ProjectVerificationOutputWindowStatus.Missing),
            _ => Refused(query, ProjectVerificationOutputWindowStatus.IntegrityMismatch),
        };
    }

    private static Result<GetProjectVerificationOutputWindowQueryResult> Refused(
        GetProjectVerificationOutputWindowQuery query, ProjectVerificationOutputWindowStatus status) =>
        Result<GetProjectVerificationOutputWindowQueryResult>.Success(
            GetProjectVerificationOutputWindowQueryResult.Refused(query, status));
}
