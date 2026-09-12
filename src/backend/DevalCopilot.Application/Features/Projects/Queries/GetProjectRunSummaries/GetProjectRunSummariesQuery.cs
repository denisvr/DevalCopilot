using Devalente.Shared.Cqrs;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectRunSummaries;

public sealed record GetProjectRunSummariesQuery : IQuery<IReadOnlyList<ProjectRunSummaryQueryResult>>;
