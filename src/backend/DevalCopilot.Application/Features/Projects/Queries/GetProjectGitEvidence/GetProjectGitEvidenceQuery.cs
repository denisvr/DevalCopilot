using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectGitEvidence;

public sealed record GetProjectGitEvidenceQuery(Guid ProjectId) : IQuery<Result<GetProjectGitEvidenceQueryResult>>;
