using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetCollaborationMessageEvidence;

/// <summary>
/// Bounded, read-only evidence about the exact <c>Attempt</c> that produced one
/// <c>CollaborationMessage</c> — resolved solely through that message's own durable
/// <c>AttemptId</c> foreign key, never through "the latest attempt of that role" or any other
/// substitute. An unknown <paramref name="RunId"/>/<paramref name="MessageId"/> pair fails with
/// <c>collaboration_messages.not_found</c>; an existing message with no linked attempt (a
/// legitimate Human/Orchestrator/Simulated message) and a message whose linked attempt row cannot
/// be found (a broken persisted link) both succeed with their own explicit, distinct result
/// status — never coalesced with each other or with not-found.
/// </summary>
public sealed record GetCollaborationMessageEvidenceQuery(Guid RunId, Guid MessageId)
    : IQuery<Result<CollaborationMessageEvidenceQueryResult>>;
