using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Projects.Queries.GetWorkspaceExecutionStatus;

/// <summary>
/// Who may execute in a project's latest owned workspace, read now from the existing durable rows through the shared exclusion policy
/// (ADR-0057). It reads no Git, provider, process table or AgentSupervision, writes nothing, and states only what this read found: never
/// permission, provider readiness or current source attestation. An unknown project is NotFound.
/// </summary>
public sealed record GetWorkspaceExecutionStatusQuery(Guid ProjectId) : IQuery<Result<GetWorkspaceExecutionStatusQueryResult>>;
