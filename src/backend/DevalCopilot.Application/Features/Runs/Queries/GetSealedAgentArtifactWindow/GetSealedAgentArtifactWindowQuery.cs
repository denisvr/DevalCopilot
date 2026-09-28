using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetSealedAgentArtifactWindow;

/// <summary>
/// One bounded, integrity-verified text window of a sealed Agent-attempt artifact — resolved
/// solely through the collaboration message's own durable <c>AttemptId</c> foreign key, exactly
/// like <c>GetCollaborationMessageEvidenceQuery</c>. An unknown <paramref name="RunId"/>/
/// <paramref name="MessageId"/> pair fails with <c>collaboration_messages.not_found</c>; every
/// other resolution outcome (no agent evidence, a broken attempt link, a purpose outside the
/// closed allowlist, no matching artifact row, a missing sealed file, or a failed integrity check)
/// succeeds with its own explicit, distinct status — never partial content, and never a
/// substituted attempt or artifact.
/// </summary>
public sealed record GetSealedAgentArtifactWindowQuery(
    Guid RunId, Guid MessageId, ArtifactPurpose Purpose, long FromOffset, int MaxBytes)
    : IQuery<Result<GetSealedAgentArtifactWindowQueryResult>>;
