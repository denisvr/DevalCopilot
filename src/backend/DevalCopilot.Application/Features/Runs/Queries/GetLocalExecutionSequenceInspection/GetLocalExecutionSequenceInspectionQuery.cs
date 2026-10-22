using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequenceInspection;

/// <summary>
/// The recorded index of one run's local execution sequence (ADR-0061): the stored bookkeeping header and every recorded step with its
/// recorded link, read from bounded untracked scalar facts and proved as a whole. It invokes no admission, ownership, eligibility or
/// mutation handler and reads no current lease, workspace, checkpoint, Git, filesystem, process, provider or account state. An unknown run
/// fails with <c>local_execution_inspection.not_found</c>.
/// </summary>
public sealed record GetLocalExecutionSequenceInspectionQuery(Guid RunId)
    : IQuery<Result<GetLocalExecutionSequenceInspectionQueryResult>>;
