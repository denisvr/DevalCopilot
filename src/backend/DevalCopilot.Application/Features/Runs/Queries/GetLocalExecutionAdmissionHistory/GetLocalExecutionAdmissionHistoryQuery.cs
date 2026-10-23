using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionAdmissionHistory;

/// <summary>
/// The recorded admission ledger of one run's queued local execution cycle (ADR-0063): the stored sequence header and every recorded
/// admission, including a wait retired without ever creating a step, with the exact linked step identity where one exists. It is read
/// and proved as a whole from one database statement. It invokes no admission, ownership, eligibility or mutation handler, reads no
/// current lease, workspace, checkpoint, recipe, Git, filesystem, process, provider, account or artifact state and proves no operation.
/// An unknown run fails with <c>local_execution_inspection.not_found</c>.
/// </summary>
public sealed record GetLocalExecutionAdmissionHistoryQuery(Guid RunId)
    : IQuery<Result<GetLocalExecutionAdmissionHistoryQueryResult>>;
