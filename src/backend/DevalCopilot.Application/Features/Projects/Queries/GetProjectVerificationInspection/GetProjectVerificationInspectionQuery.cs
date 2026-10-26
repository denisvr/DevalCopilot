using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationInspection;

/// <summary>
/// The exact recorded metadata of one project verification execution and its two sealed-stream descriptors (ADR-0069). An unknown project
/// and an absent or foreign execution fail with <c>project_verification_inspection.not_found</c>; a member whose recorded facts are
/// incoherent is Unavailable, never absent. It opens no artifact file.
/// </summary>
public sealed record GetProjectVerificationInspectionQuery(Guid ProjectId, Guid ExecutionId)
    : IQuery<Result<GetProjectVerificationInspectionQueryResult>>;
