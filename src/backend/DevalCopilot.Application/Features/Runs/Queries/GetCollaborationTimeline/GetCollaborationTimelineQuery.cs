using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationTimeline;

public sealed record GetCollaborationTimelineQuery(Guid RunId) : IQuery<Result<IReadOnlyList<CollaborationMessageTimelineQueryResult>>>;
