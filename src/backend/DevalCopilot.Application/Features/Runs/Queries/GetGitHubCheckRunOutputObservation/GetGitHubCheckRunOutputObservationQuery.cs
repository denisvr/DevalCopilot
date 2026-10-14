using Devalente.Shared.Cqrs;
using Devalente.Shared.Results;

namespace DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckRunOutputObservation;

/// <summary>
/// Requests one explicit, ephemeral inspection of the provider-supplied output (title, summary and text) of one GitHub check run
/// associated with the recorded local commit of a run whose repository identity is bound (ADR-0047). The only client input is the
/// opaque check run identifier: the repository, commit and branch come from the recorded source, target and binding rows. It writes,
/// locks, persists and discovers nothing and grants no approval, permission or publication authority.
/// </summary>
public sealed record GetGitHubCheckRunOutputObservationQuery(Guid RunId, string CheckRunId)
    : IQuery<Result<GetGitHubCheckRunOutputObservationQueryResult>>;
