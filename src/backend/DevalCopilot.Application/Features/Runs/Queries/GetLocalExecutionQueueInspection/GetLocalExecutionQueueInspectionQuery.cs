using Devalente.Shared.Cqrs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionQueueInspection;

/// <summary>
/// The recorded outstanding admissions of every queued local execution cycle in the authoritative database (ADR-0064): each Waiting,
/// Reserved or Unresolved admission with its proved sequence header and registered project label, read and proved from one database
/// statement. Empty never means no active cycle, no owner or free capacity. It invokes no admission, ownership, eligibility or mutation
/// handler, reads no live lease, workspace, Git, filesystem, process, provider, account or artifact state and proves no operation.
/// </summary>
public sealed record GetLocalExecutionQueueInspectionQuery : IQuery<GetLocalExecutionQueueInspectionQueryResult>;
