using Devalente.Shared.Cqrs;

namespace DevalCopilot.Application.Features.Runs.Queries.GetEligibleChallengeResolutionAttempts;

public sealed record GetEligibleChallengeResolutionAttemptsQuery : IQuery<IReadOnlyList<EligibleChallengeResolutionAttempt>>;
