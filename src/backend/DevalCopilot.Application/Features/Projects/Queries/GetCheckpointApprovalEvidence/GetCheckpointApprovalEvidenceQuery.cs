using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Projects.Queries.GetCheckpointApprovalEvidence;

/// <summary>Read-only: returns the complete verification set a human may approve for the exact current checkpoint, or a fixed
/// refusal. It writes nothing, claims nothing, invokes no provider and authorizes nothing; the review command decides again.</summary>
public sealed record GetCheckpointApprovalEvidenceQuery(Guid ProjectId, Guid CheckpointId)
    : IQuery<Result<CheckpointApprovalEvidenceQueryResult>>;
