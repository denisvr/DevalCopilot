using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Features.Projects.Queries.GetVerificationExecutionOutput;

public sealed record GetVerificationExecutionOutputQuery(
    Guid ProjectId,
    Guid VerificationExecutionId,
    VerificationOutputPurpose Purpose,
    long FromOffset,
    int MaxBytes) : IQuery<Result<VerificationExecutionOutputQueryResult>>;
