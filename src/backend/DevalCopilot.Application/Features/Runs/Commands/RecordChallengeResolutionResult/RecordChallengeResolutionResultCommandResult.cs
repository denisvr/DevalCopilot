using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Runs.Commands.RecordChallengeResolutionResult;

public sealed record RecordChallengeResolutionResultCommandResult(AttemptStatus Status, long LatestEventSequence);
