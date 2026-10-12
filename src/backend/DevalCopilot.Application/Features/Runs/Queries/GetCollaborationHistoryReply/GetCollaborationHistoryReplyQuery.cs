using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationHistoryReply;

/// <summary>
/// The recorded reply parent of exactly one recorded collaboration message of a run (ADR-0036). It reads the source's own stored
/// header and reference, resolves at most that one parent (same run, strictly earlier, version 1.0, a type the existing reply policy
/// allows) and never substitutes another message, drains pages or follows a chain. It writes, claims and repairs nothing. An unknown
/// run or a missing or foreign source fails with <c>collaboration_reply.not_found</c>.
/// </summary>
public sealed record GetCollaborationHistoryReplyQuery(Guid RunId, Guid MessageId)
    : IQuery<Result<GetCollaborationHistoryReplyQueryResult>>;
