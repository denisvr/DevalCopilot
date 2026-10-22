using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionInspection;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionStepInspection;

public sealed class GetLocalExecutionStepInspectionQueryHandler(IDevalCopilotDbContext dbContext)
    : IQueryHandler<GetLocalExecutionStepInspectionQuery, Result<GetLocalExecutionStepInspectionQueryResult>>
{
    /// <summary>At most this many matching metadata rows are read per purpose, so ambiguity is detected without unbounded reads.</summary>
    private const int MetadataRowsPerPurpose = 3;

    public async Task<Result<GetLocalExecutionStepInspectionQueryResult>> HandleAsync(
        GetLocalExecutionStepInspectionQuery query, CancellationToken cancellationToken)
    {
        var reading = await LocalExecutionInspectionReader.ReadAsync(dbContext, query.RunId, cancellationToken);
        if (reading.Kind is LocalExecutionInspectionReadingKind.RunNotFound or LocalExecutionInspectionReadingKind.NotRecorded
            || reading.SequenceId is not { } sequenceId
            || !await LocalExecutionInspectionReader.HasStepAsync(dbContext, sequenceId, query.StepId, cancellationToken))
        {
            return Result<GetLocalExecutionStepInspectionQueryResult>.Failure(LocalExecutionInspectionErrors.NotFound());
        }

        if (reading.Structure is not { } structure)
        {
            return Refused(query, sequenceId, LocalExecutionStepInspectionStatus.Unavailable);
        }

        var step = structure.Steps.FirstOrDefault(candidate => candidate.StepId == query.StepId);
        if (step is null)
        {
            return Refused(query, sequenceId, LocalExecutionStepInspectionStatus.Unavailable);
        }

        if (step.OperationId is null)
        {
            return Refused(query, sequenceId, LocalExecutionStepInspectionStatus.NoOperation);
        }

        if (LocalExecutionSequencePolicy.IsAgentKind(step.Kind))
        {
            var header = await LocalExecutionOperationProof.ProveAgentAsync(dbContext, structure, step, cancellationToken);
            return header is null
                ? Refused(query, sequenceId, LocalExecutionStepInspectionStatus.Unavailable)
                : Result<GetLocalExecutionStepInspectionQueryResult>.Success(new GetLocalExecutionStepInspectionQueryResult(
                    query.RunId, sequenceId, query.StepId, LocalExecutionStepInspectionStatus.Recorded, step, header, null));
        }

        var facts = await LocalExecutionOperationProof.ProveVerificationAsync(dbContext, structure, step, cancellationToken);
        if (facts is null)
        {
            return Refused(query, sequenceId, LocalExecutionStepInspectionStatus.Unavailable);
        }

        // Metadata only, of exactly the proved execution's allowlisted streams: never a path, hash or byte, and each stream is admitted
        // independently, so a bad one does not hide its sibling.
        var standardOutput = await DescribeAsync(facts.ExecutionId, VerificationOutputPurpose.StandardOutput, cancellationToken);
        var standardError = await DescribeAsync(facts.ExecutionId, VerificationOutputPurpose.StandardError, cancellationToken);
        return Result<GetLocalExecutionStepInspectionQueryResult>.Success(new GetLocalExecutionStepInspectionQueryResult(
            query.RunId,
            sequenceId,
            query.StepId,
            LocalExecutionStepInspectionStatus.Recorded,
            step,
            null,
            new LocalExecutionVerificationInspection(facts, standardOutput, standardError)));
    }

    private async Task<RecordedVerificationOutputDescriptor> DescribeAsync(
        Guid executionId, VerificationOutputPurpose purpose, CancellationToken cancellationToken)
    {
        var purposeCode = RecordedVerificationOutputAdmission.PurposeCode(purpose);
        var rows = await dbContext.VerificationOutputArtifacts.AsNoTracking()
            .Where(output => output.VerificationExecutionId == executionId && output.Purpose == purpose)
            .Take(MetadataRowsPerPurpose)
            .ProjectDescriptor()
            .ToArrayAsync(cancellationToken);
        return rows.Any(row => row.ExecutionId != executionId || row.PurposeCode != purposeCode)
            ? RecordedVerificationOutputDescriptor.Unavailable
            : RecordedVerificationOutputAdmission.Admit(rows);
    }

    private static Result<GetLocalExecutionStepInspectionQueryResult> Refused(
        GetLocalExecutionStepInspectionQuery query, Guid sequenceId, LocalExecutionStepInspectionStatus status) =>
        Result<GetLocalExecutionStepInspectionQueryResult>.Success(
            GetLocalExecutionStepInspectionQueryResult.Without(query.RunId, sequenceId, query.StepId, status));
}
